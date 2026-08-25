using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Diagnostics;

namespace PrintableBook.Desktop.BackgroundTasks;

public sealed class BackgroundTaskManager(
    IServiceProvider serviceProvider,
    IOperationDiagnostics diagnostics) : IBackgroundTaskManager, IDisposable
{
    private const int MaximumTerminalHistory = 100;
    private readonly Lock sync = new();
    private readonly Dictionary<BackgroundTaskId, BackgroundTaskEntry> registry = [];
    private readonly Dictionary<BackgroundTaskLaneKind, BackgroundTaskLane> lanes = BackgroundTaskPolicies.All.Values
        .GroupBy(policy => policy.Lane)
        .ToDictionary(group => group.Key, group => new BackgroundTaskLane(group.First().MaximumConcurrency));
    private readonly Queue<BackgroundTaskId> terminalOrder = [];
    private long nextSequence;
    private bool disposed;

    public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(request);

        BackgroundTaskEntry entry;
        BackgroundTaskLaneKind laneKind;
        lock (sync)
        {
            ThrowIfDisposed();
            var policy = BackgroundTaskPolicies.For(kind);
            var duplicate = FindActiveDuplicateLocked(kind, policy.DuplicatePolicy);
            if (duplicate is not null) return ValueTask.FromResult(SnapshotLocked(duplicate));

            entry = new BackgroundTaskEntry
            {
                TaskId = BackgroundTaskId.New(),
                Kind = kind,
                Key = key,
                Subject = subject,
                Request = request!,
                View = initialView,
                State = BackgroundTaskState.Queued,
                Cancellation = new CancellationTokenSource(),
                Sequence = ++nextSequence
            };
            registry.Add(entry.TaskId, entry);
            lanes[policy.Lane].Queue.Enqueue(entry.TaskId);
            laneKind = policy.Lane;
        }

        diagnostics.Record("task.queued", subject, kind.ToString());
        TryDispatch(laneKind);
        return ValueTask.FromResult(GetSnapshot(entry.TaskId)!);
    }

    public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetSnapshot(taskId));
    }

    public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            IReadOnlyList<BackgroundTaskSnapshot> snapshots = registry.Values
                .Where(entry => kind is null || entry.Kind == kind)
                .OrderByDescending(entry => entry.Sequence)
                .Select(SnapshotLocked)
                .ToArray();
            return ValueTask.FromResult(snapshots);
        }
    }

    public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource? source = null;
        object? cancellationSync = null;
        BackgroundTaskEntry? cancellationEntry = null;
        BackgroundTaskLaneKind? dispatchLane = null;
        string? lifecycleEvent = null;
        string? lifecycleSubject = null;
        string? lifecycleDetail = null;
        BackgroundTaskSnapshot? snapshot;
        lock (sync)
        {
            if (!registry.TryGetValue(taskId, out var entry)) return ValueTask.FromResult<BackgroundTaskSnapshot?>(null);
            if (entry.State == BackgroundTaskState.Queued)
            {
                entry.State = BackgroundTaskState.Cancelled;
                entry.FinishedAt = DateTimeOffset.UtcNow;
                entry.Terminal.TrySetResult();
                AddTerminalLocked(entry);
                source = entry.Cancellation;
                cancellationSync = entry.CancellationSync;
                lifecycleEvent = "task.cancelled";
                lifecycleSubject = entry.Subject;
                lifecycleDetail = entry.Kind.ToString();
                dispatchLane = BackgroundTaskPolicies.For(entry.Kind).Lane;
            }
            else if (entry.State == BackgroundTaskState.Running)
            {
                entry.State = BackgroundTaskState.Cancelling;
                cancellationEntry = entry;
                lifecycleEvent = "task.cancelling";
                lifecycleSubject = entry.Subject;
                lifecycleDetail = entry.Kind.ToString();
            }
            snapshot = SnapshotLocked(entry);
        }

        if (lifecycleEvent is not null) diagnostics.Record(lifecycleEvent, lifecycleSubject, lifecycleDetail);
        if (source is not null && cancellationSync is not null)
        {
            lock (cancellationSync)
            {
                try
                {
                    source.Dispose();
                }
                catch (ObjectDisposedException) { }
            }
        }
        if (cancellationEntry is not null) BeginCancellationSignal(cancellationEntry);
        if (dispatchLane is not null) TryDispatch(dispatchLane.Value);
        return ValueTask.FromResult<BackgroundTaskSnapshot?>(snapshot);
    }

    public async ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task? terminal;
        lock (sync) terminal = registry.TryGetValue(taskId, out var entry) ? entry.Terminal.Task : null;
        if (terminal is null) return false;
        try
        {
            await terminal.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
    {
        lock (sync)
        {
            if (registry.TryGetValue(taskId, out var entry) && IsTerminal(entry.State) && entry.Result is TResult typed)
            {
                result = typed;
                return true;
            }
        }
        result = default;
        return false;
    }

    public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? view) where TView : class
    {
        lock (sync)
        {
            if (registry.TryGetValue(taskId, out var entry) && entry.View is TView typed)
            {
                view = typed;
                return true;
            }
        }
        view = default;
        return false;
    }

    public void Dispose()
    {
        List<BackgroundTaskEntry> cancellationEntries = [];
        List<BackgroundTaskEntry> queuedEntries = [];
        lock (sync)
        {
            if (disposed) return;
            disposed = true;

            var activeEntries = registry.Values.Where(entry => !IsTerminal(entry.State)).ToArray();
            foreach (var entry in activeEntries)
            {
                switch (entry.State)
                {
                    case BackgroundTaskState.Queued:
                        entry.State = BackgroundTaskState.Cancelled;
                        entry.FinishedAt = DateTimeOffset.UtcNow;
                        entry.Terminal.TrySetResult();
                        AddTerminalLocked(entry);
                        queuedEntries.Add(entry);
                        break;
                    case BackgroundTaskState.Running:
                        entry.State = BackgroundTaskState.Cancelling;
                        cancellationEntries.Add(entry);
                        break;
                    case BackgroundTaskState.Cancelling:
                        cancellationEntries.Add(entry);
                        break;
                }
            }
        }

        foreach (var entry in queuedEntries)
        {
            lock (entry.CancellationSync)
            {
                try { entry.Cancellation.Dispose(); }
                catch (ObjectDisposedException) { }
            }
            diagnostics.Record("task.cancelled", entry.Subject, entry.Kind.ToString());
        }

        foreach (var entry in cancellationEntries)
        {
            diagnostics.Record("task.cancelling", entry.Subject, entry.Kind.ToString());
            BeginCancellationSignal(entry);
        }
    }

    private void TryDispatch(BackgroundTaskLaneKind laneKind)
    {
        lock (sync)
        {
            if (disposed) return;
            var lane = lanes[laneKind];
            while (lane.ActiveCount < lane.MaximumConcurrency && lane.Queue.TryDequeue(out var taskId))
            {
                if (!registry.TryGetValue(taskId, out var entry) || entry.State != BackgroundTaskState.Queued) continue;
                entry.State = BackgroundTaskState.Running;
                entry.StartedAt = DateTimeOffset.UtcNow;
                lane.ActiveCount++;
                StartExecution(entry);
            }
        }
    }

    private void StartExecution(BackgroundTaskEntry entry)
    {
        diagnostics.Record("task.started", entry.Subject, entry.Kind.ToString());
        entry.ExecutionTask = Task.Run(() => ExecuteEntryAsync(entry), CancellationToken.None);
    }

    private void BeginCancellationSignal(BackgroundTaskEntry entry)
    {
        if (Interlocked.CompareExchange(ref entry.CancellationSignalStarted, 1, 0) != 0) return;

        var signal = Task.Run(
            () =>
            {
                lock (entry.CancellationSync)
                {
                    try
                    {
                        entry.Cancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The worker completed before the scheduled cancellation signal acquired this lock.
                    }
                }
            },
            CancellationToken.None);

        entry.CancellationSignalTask = signal;

        _ = signal.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ExecuteEntryAsync(BackgroundTaskEntry entry)
    {
        BackgroundTaskState terminal = BackgroundTaskState.Completed;
        string? errorCode = null;
        string? errorMessage = null;
        object? result = null;
        try
        {
            var worker = serviceProvider.GetRequiredKeyedService<IBackgroundTaskWorker>(entry.Kind);
            if (worker.Kind != entry.Kind || !worker.RequestType.IsInstanceOfType(entry.Request))
            {
                throw new BackgroundTaskFailureException("background_task_worker_mismatch", "Background task worker configuration is invalid.");
            }
            using var operation = diagnostics.Begin($"worker.{entry.Kind}", entry.Subject);
            result = await worker.ExecuteAsync(entry.Request, new BackgroundTaskContext(entry.TaskId, Report, SetView), entry.Cancellation.Token);
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            terminal = BackgroundTaskState.Cancelled;
        }
        catch (BackgroundTaskFailureException failure)
        {
            terminal = BackgroundTaskState.Failed;
            errorCode = failure.Code;
            errorMessage = failure.Message;
        }
        catch (Exception)
        {
            terminal = BackgroundTaskState.Failed;
            errorCode = "background_task_failed";
            errorMessage = "Background task failed.";
        }
        finally
        {
            var laneKind = BackgroundTaskPolicies.For(entry.Kind).Lane;
            lock (sync)
            {
                entry.Result = result;
                entry.State = terminal;
                entry.ErrorCode = errorCode;
                entry.ErrorMessage = errorMessage;
                entry.FinishedAt = DateTimeOffset.UtcNow;
                lanes[laneKind].ActiveCount--;
                entry.Terminal.TrySetResult();
                AddTerminalLocked(entry);
            }
            lock (entry.CancellationSync) entry.Cancellation.Dispose();
            diagnostics.Record(terminal switch
            {
                BackgroundTaskState.Completed => "task.completed",
                BackgroundTaskState.Cancelled => "task.cancelled",
                _ => "task.failed"
            }, entry.Subject, errorCode ?? entry.Kind.ToString());
            TryDispatch(laneKind);
        }
    }

    private void Report(BackgroundTaskId taskId, string step, int? completed, int? total, string? detail, string? subject)
    {
        lock (sync)
        {
            if (!registry.TryGetValue(taskId, out var entry) || IsTerminal(entry.State)) return;
            entry.Step = step;
            entry.Completed = completed;
            entry.Total = total;
            entry.Detail = detail;
            if (subject is not null) entry.Subject = subject;
        }
    }

    private void SetView(BackgroundTaskId taskId, object view)
    {
        lock (sync)
        {
            if (registry.TryGetValue(taskId, out var entry) && !IsTerminal(entry.State)) entry.View = view;
        }
    }

    private BackgroundTaskEntry? FindActiveDuplicateLocked(BackgroundTaskKind kind, BackgroundTaskDuplicatePolicy policy) => policy switch
    {
        BackgroundTaskDuplicatePolicy.JoinByKind or BackgroundTaskDuplicatePolicy.ReturnExisting => registry.Values.FirstOrDefault(entry =>
            entry.Kind == kind && !IsTerminal(entry.State)),
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported background task duplicate policy.")
    };

    private BackgroundTaskSnapshot? GetSnapshot(BackgroundTaskId taskId)
    {
        lock (sync) return registry.TryGetValue(taskId, out var entry) ? SnapshotLocked(entry) : null;
    }

    private static BackgroundTaskSnapshot SnapshotLocked(BackgroundTaskEntry entry) => new(entry.TaskId, entry.Kind, entry.State, entry.Key, entry.Subject, entry.Step, entry.Completed, entry.Total, entry.Detail, entry.StartedAt, entry.FinishedAt, entry.ErrorCode, entry.ErrorMessage);

    private void AddTerminalLocked(BackgroundTaskEntry entry)
    {
        terminalOrder.Enqueue(entry.TaskId);
        while (terminalOrder.Count > MaximumTerminalHistory)
        {
            var expired = terminalOrder.Dequeue();
            if (!registry.TryGetValue(expired, out var candidate) || !IsTerminal(candidate.State)) continue;
            if (IsLatestRetainedKindLocked(candidate))
            {
                terminalOrder.Enqueue(expired);
                continue;
            }
            registry.Remove(expired);
        }
    }

    private bool IsLatestRetainedKindLocked(BackgroundTaskEntry candidate) => candidate.Kind is (BackgroundTaskKind.LibraryRefresh or BackgroundTaskKind.ProcessingSession) &&
        !registry.Values.Any(entry => entry.Kind == candidate.Kind && IsTerminal(entry.State) && entry.Sequence > candidate.Sequence);

    private static bool IsTerminal(BackgroundTaskState state) => state is BackgroundTaskState.Completed or BackgroundTaskState.Failed or BackgroundTaskState.Cancelled;
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(BackgroundTaskManager)); }
}
