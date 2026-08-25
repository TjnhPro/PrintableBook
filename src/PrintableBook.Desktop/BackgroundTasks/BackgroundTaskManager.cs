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
            var duplicate = FindActiveDuplicateLocked(kind, key, policy.DuplicatePolicy);
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
        BackgroundTaskLaneKind? dispatchLane = null;
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
                dispatchLane = BackgroundTaskPolicies.For(entry.Kind).Lane;
            }
            else if (entry.State == BackgroundTaskState.Running)
            {
                entry.State = BackgroundTaskState.Cancelling;
                source = entry.Cancellation;
                cancellationSync = entry.CancellationSync;
            }
            snapshot = SnapshotLocked(entry);
        }

        if (source is not null && cancellationSync is not null)
        {
            lock (cancellationSync)
            {
                try
                {
                    if (snapshot?.State == BackgroundTaskState.Cancelling) source.Cancel();
                    else source.Dispose();
                }
                catch (ObjectDisposedException) { }
            }
        }
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
        (CancellationTokenSource Source, object Sync)[] sources;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            sources = registry.Values.Where(entry => !IsTerminal(entry.State)).Select(entry => (entry.Cancellation, entry.CancellationSync)).ToArray();
        }
        foreach (var source in sources)
        {
            lock (source.Sync)
            {
                try { source.Source.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private void TryDispatch(BackgroundTaskLaneKind laneKind)
    {
        List<BackgroundTaskEntry> dispatch = [];
        lock (sync)
        {
            var lane = lanes[laneKind];
            while (lane.ActiveCount < lane.MaximumConcurrency && lane.Queue.TryDequeue(out var taskId))
            {
                if (!registry.TryGetValue(taskId, out var entry) || entry.State != BackgroundTaskState.Queued) continue;
                entry.State = BackgroundTaskState.Running;
                entry.StartedAt = DateTimeOffset.UtcNow;
                lane.ActiveCount++;
                dispatch.Add(entry);
            }
        }
        foreach (var entry in dispatch) StartExecution(entry);
    }

    private void StartExecution(BackgroundTaskEntry entry) => entry.ExecutionTask = Task.Run(() => ExecuteEntryAsync(entry), CancellationToken.None);

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

    private BackgroundTaskEntry? FindActiveDuplicateLocked(BackgroundTaskKind kind, string key, BackgroundTaskDuplicatePolicy policy) => registry.Values.FirstOrDefault(entry =>
        entry.Kind == kind && !IsTerminal(entry.State) &&
        (policy is BackgroundTaskDuplicatePolicy.JoinByKind or BackgroundTaskDuplicatePolicy.ReturnExisting || entry.Key == key));

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
