using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Desktop.BackgroundTasks;

namespace PrintableBook.Desktop.Tests.BackgroundTasks;

public sealed class BackgroundTaskManagerTests
{
    [Fact]
    public void V1_policies_define_exactly_the_three_supported_task_kinds_and_lane_limits()
    {
        Assert.Equal(
            [BackgroundTaskKind.LibraryRefresh, BackgroundTaskKind.ProcessingSession, BackgroundTaskKind.AssetPreview],
            BackgroundTaskPolicies.All.Keys.Order());

        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Library, 1, BackgroundTaskDuplicatePolicy.JoinByKind), BackgroundTaskPolicies.For(BackgroundTaskKind.LibraryRefresh));
        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Processing, 1, BackgroundTaskDuplicatePolicy.ReturnExisting), BackgroundTaskPolicies.For(BackgroundTaskKind.ProcessingSession));
        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Preview, 2, BackgroundTaskDuplicatePolicy.JoinByKey), BackgroundTaskPolicies.For(BackgroundTaskKind.AssetPreview));
    }

    [Fact]
    public async Task StartAsync_applies_the_locked_duplicate_policies_before_worker_execution()
    {
        var library = new BlockingWorker(BackgroundTaskKind.LibraryRefresh);
        var processing = new BlockingWorker(BackgroundTaskKind.ProcessingSession);
        var preview = new BlockingWorker(BackgroundTaskKind.AssetPreview);
        using var manager = CreateManager(library, processing, preview);

        var libraryFirst = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "first", "Library", new TaskRequest("first"));
        await library.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var librarySecond = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "second", "Library", new TaskRequest("second"));
        var processFirst = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "one", null, new TaskRequest("one"));
        await processing.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var processSecond = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "two", null, new TaskRequest("two"));
        var previewFirst = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "page-1", null, new TaskRequest("page-1"));
        var previewDuplicate = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "page-1", null, new TaskRequest("page-1"));
        var previewDistinct = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "page-2", null, new TaskRequest("page-2"));

        Assert.Equal(libraryFirst.TaskId, librarySecond.TaskId);
        Assert.Equal(processFirst.TaskId, processSecond.TaskId);
        Assert.Equal(previewFirst.TaskId, previewDuplicate.TaskId);
        Assert.NotEqual(previewFirst.TaskId, previewDistinct.TaskId);

        library.Release.TrySetResult();
        processing.Release.TrySetResult();
        preview.Release.TrySetResult();
    }

    [Fact]
    public async Task Lanes_apply_independent_limits_and_preview_dispatch_is_fifo()
    {
        var library = new BlockingWorker(BackgroundTaskKind.LibraryRefresh);
        var processing = new BlockingWorker(BackgroundTaskKind.ProcessingSession);
        var preview = new BlockingWorker(BackgroundTaskKind.AssetPreview);
        using var manager = CreateManager(library, processing, preview);

        await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
        await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "processing", null, new TaskRequest("processing"));
        var first = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "one", null, new TaskRequest("one"));
        var second = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "two", null, new TaskRequest("two"));
        var third = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "three", null, new TaskRequest("three"));
        await Task.WhenAll(library.Started.Task, processing.Started.Task, preview.StartedTwo.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal(1, library.MaximumActive);
        Assert.Equal(1, processing.MaximumActive);
        Assert.Equal(2, preview.MaximumActive);
        Assert.Equal(BackgroundTaskState.Queued, (await manager.GetAsync(third.TaskId))!.State);

        preview.ReleaseOne();
        await preview.StartedThree.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("three", preview.StartedRequests[^1]);

        library.Release.TrySetResult();
        processing.Release.TrySetResult();
        preview.Release.TrySetResult();
        Assert.True(await manager.WaitAsync(first.TaskId, TimeSpan.FromSeconds(2)));
        Assert.True(await manager.WaitAsync(second.TaskId, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Completion_failure_and_cancellation_are_ram_observable_without_cancelling_a_waiter_worker()
    {
        var library = new BlockingWorker(BackgroundTaskKind.LibraryRefresh);
        var processing = new BlockingWorker(BackgroundTaskKind.ProcessingSession);
        var preview = new BlockingWorker(BackgroundTaskKind.AssetPreview);
        using var manager = CreateManager(library, processing, preview);

        var accepted = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"), new TaskView("initial"));
        await library.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(manager.TryGetView<TaskView>(accepted.TaskId, out var view));
        Assert.Equal("initial", view!.Value);
        Assert.False(await manager.WaitAsync(accepted.TaskId, TimeSpan.FromMilliseconds(10)));
        Assert.Equal(BackgroundTaskState.Running, (await manager.GetAsync(accepted.TaskId))!.State);

        library.Release.TrySetResult();
        Assert.True(await manager.WaitAsync(accepted.TaskId, TimeSpan.FromSeconds(2)));
        Assert.True(manager.TryGetResult<string>(accepted.TaskId, out var result));
        Assert.Equal("library", result);
        Assert.False(manager.TryGetResult<int>(accepted.TaskId, out _));
    }

    [Fact]
    public async Task Worker_failure_is_sanitized_and_safe_failure_details_are_preserved()
    {
        using var unexpected = CreateManager(new ThrowingWorker(new InvalidOperationException("D:\\secret")), new BlockingWorker(BackgroundTaskKind.ProcessingSession), new BlockingWorker(BackgroundTaskKind.AssetPreview));
        var unexpectedTask = await unexpected.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
        Assert.True(await unexpected.WaitAsync(unexpectedTask.TaskId, TimeSpan.FromSeconds(2)));
        var unexpectedSnapshot = await unexpected.GetAsync(unexpectedTask.TaskId);
        Assert.Equal(BackgroundTaskState.Failed, unexpectedSnapshot!.State);
        Assert.Equal("background_task_failed", unexpectedSnapshot.ErrorCode);
        Assert.Equal("Background task failed.", unexpectedSnapshot.ErrorMessage);

        using var safe = CreateManager(new ThrowingWorker(new BackgroundTaskFailureException("refresh_failed", "Library cannot be read.")), new BlockingWorker(BackgroundTaskKind.ProcessingSession), new BlockingWorker(BackgroundTaskKind.AssetPreview));
        var safeTask = await safe.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
        Assert.True(await safe.WaitAsync(safeTask.TaskId, TimeSpan.FromSeconds(2)));
        var safeSnapshot = await safe.GetAsync(safeTask.TaskId);
        Assert.Equal("refresh_failed", safeSnapshot!.ErrorCode);
        Assert.Equal("Library cannot be read.", safeSnapshot.ErrorMessage);
    }

    [Fact]
    public async Task Queued_cancel_never_executes_worker_and_running_cancel_transitions_immediately()
    {
        var library = new BlockingWorker(BackgroundTaskKind.LibraryRefresh);
        var processing = new BlockingWorker(BackgroundTaskKind.ProcessingSession);
        var preview = new BlockingWorker(BackgroundTaskKind.AssetPreview);
        using var manager = CreateManager(library, processing, preview);

        var first = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "first", null, new TaskRequest("first"));
        var second = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "second", null, new TaskRequest("second"));
        var queued = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "queued", null, new TaskRequest("queued"));
        await preview.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queuedCancelled = await manager.CancelAsync(queued.TaskId);

        Assert.Equal(BackgroundTaskState.Cancelled, queuedCancelled!.State);
        Assert.Equal(BackgroundTaskState.Cancelled, (await manager.GetAsync(queued.TaskId))!.State);
        Assert.DoesNotContain("queued", preview.StartedRequests);

        var runningCancelled = await manager.CancelAsync(first.TaskId);
        Assert.Equal(BackgroundTaskState.Cancelling, runningCancelled!.State);
        Assert.True(await manager.WaitAsync(first.TaskId, TimeSpan.FromSeconds(2)));
        Assert.Equal(BackgroundTaskState.Cancelled, (await manager.GetAsync(first.TaskId))!.State);
        preview.Release.TrySetResult();
        Assert.True(await manager.WaitAsync(second.TaskId, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Worker_execution_isolated_from_the_callers_synchronization_context()
    {
        var worker = new ContextWorker();
        using var manager = CreateManager(worker, new BlockingWorker(BackgroundTaskKind.ProcessingSession), new BlockingWorker(BackgroundTaskKind.AssetPreview));
        var previous = SynchronizationContext.Current;
        var marker = new MarkerSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(marker);
            var task = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
            await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotSame(marker, worker.ExecutionContext);
            Assert.Equal(BackgroundTaskState.Running, (await manager.GetAsync(task.TaskId))!.State);
        }
        finally
        {
            worker.Release.TrySetResult();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task Lifecycle_diagnostics_are_recorded_in_queue_start_terminal_order()
    {
        var diagnostics = new RecordingDiagnostics();
        using var manager = CreateManager(diagnostics, new CompletingWorker(), new BlockingWorker(BackgroundTaskKind.ProcessingSession), new BlockingWorker(BackgroundTaskKind.AssetPreview));

        var task = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", "library", new TaskRequest("library"));
        Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));

        Assert.Equal(["task.queued", "task.started", "task.completed"], diagnostics.EventsFor("library"));
    }

    [Fact]
    public async Task Failure_and_queued_cancellation_keep_their_lifecycle_order()
    {
        var diagnostics = new RecordingDiagnostics();
        var preview = new BlockingWorker(BackgroundTaskKind.AssetPreview);
        using var manager = CreateManager(diagnostics, new ThrowingWorker(new BackgroundTaskFailureException("failed", "failed")), new BlockingWorker(BackgroundTaskKind.ProcessingSession), preview);

        var failed = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "failed", "failed", new TaskRequest("failed"));
        Assert.True(await manager.WaitAsync(failed.TaskId, TimeSpan.FromSeconds(2)));

        await manager.StartAsync(BackgroundTaskKind.AssetPreview, "preview-1", "preview-1", new TaskRequest("preview-1"));
        await manager.StartAsync(BackgroundTaskKind.AssetPreview, "preview-2", "preview-2", new TaskRequest("preview-2"));
        var queued = await manager.StartAsync(BackgroundTaskKind.AssetPreview, "queued", "queued", new TaskRequest("queued"));
        await preview.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await manager.CancelAsync(queued.TaskId);

        Assert.Equal(["task.queued", "task.started", "task.failed"], diagnostics.EventsFor("failed"));
        Assert.Equal(["task.queued", "task.cancelled"], diagnostics.EventsFor("queued"));
        preview.Release.TrySetResult();
    }

    [Fact]
    public async Task Cancel_and_dispose_are_safe_while_a_cancellation_callback_is_blocked()
    {
        var cancellationWorker = new BlockingCancellationWorker();
        var processing = new BlockingWorker(BackgroundTaskKind.ProcessingSession);
        var preview = new BlockingWorker(BackgroundTaskKind.AssetPreview);
        var manager = CreateManager(cancellationWorker, processing, preview);
        try
        {
            var task = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
            await cancellationWorker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var cancel = Task.Run(async () => await manager.CancelAsync(task.TaskId));
            await cancellationWorker.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var dispose = Task.Run(manager.Dispose);

            cancellationWorker.ReleaseCallback.TrySetResult();
            await Task.WhenAll(cancel, dispose).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
            Assert.Equal(BackgroundTaskState.Cancelled, (await manager.GetAsync(task.TaskId))!.State);
        }
        finally
        {
            cancellationWorker.ReleaseCallback.TrySetResult();
            manager.Dispose();
        }
    }

    [Fact]
    public async Task Terminal_history_is_bounded_while_retaining_the_latest_library_and_processing_tasks()
    {
        using var manager = CreateManager(
            new ImmediateWorker(BackgroundTaskKind.LibraryRefresh),
            new ImmediateWorker(BackgroundTaskKind.ProcessingSession),
            new ImmediateWorker(BackgroundTaskKind.AssetPreview));

        var library = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
        var processing = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "processing", null, new TaskRequest("processing"));
        await manager.WaitAsync(library.TaskId, TimeSpan.FromSeconds(2));
        await manager.WaitAsync(processing.TaskId, TimeSpan.FromSeconds(2));
        for (var index = 0; index < 120; index++)
        {
            var preview = await manager.StartAsync(BackgroundTaskKind.AssetPreview, $"preview-{index}", null, new TaskRequest($"preview-{index}"));
            Assert.True(await manager.WaitAsync(preview.TaskId, TimeSpan.FromSeconds(2)));
        }

        var tasks = await manager.ListAsync();
        Assert.InRange(tasks.Count, 1, 102);
        Assert.Contains(tasks, task => task.TaskId == library.TaskId && task.State == BackgroundTaskState.Completed);
        Assert.Contains(tasks, task => task.TaskId == processing.TaskId && task.State == BackgroundTaskState.Completed);
    }

    private static BackgroundTaskManager CreateManager(params IBackgroundTaskWorker[] workers)
        => CreateManager(new NullDiagnostics(), workers);

    private static BackgroundTaskManager CreateManager(IOperationDiagnostics diagnostics, params IBackgroundTaskWorker[] workers)
    {
        var services = new ServiceCollection();
        foreach (var worker in workers) services.AddKeyedSingleton<IBackgroundTaskWorker>(worker.Kind, worker);
        return new BackgroundTaskManager(services.BuildServiceProvider(), diagnostics);
    }

    private sealed record TaskRequest(string Value);
    private sealed record TaskView(string Value);

    private sealed class BlockingWorker(BackgroundTaskKind kind) : BackgroundTaskWorker<TaskRequest, string>
    {
        private int active;
        public override BackgroundTaskKind Kind => kind;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartedTwo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartedThree { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumActive { get; private set; }
        public List<string> StartedRequests { get; } = [];

        public void ReleaseOne() => Release.TrySetResult();

        protected override async ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            MaximumActive = Math.Max(MaximumActive, current);
            StartedRequests.Add(request.Value);
            Started.TrySetResult();
            if (StartedRequests.Count >= 2) StartedTwo.TrySetResult();
            if (StartedRequests.Count >= 3) StartedThree.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return request.Value;
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }

    private sealed class ThrowingWorker(Exception exception) : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.LibraryRefresh;

        protected override ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken) => ValueTask.FromException<string>(exception);
    }

    private sealed class CompletingWorker : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.LibraryRefresh;
        protected override ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken) => ValueTask.FromResult(request.Value);
    }

    private sealed class BlockingCancellationWorker : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.LibraryRefresh;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CallbackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCallback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
            {
                CallbackEntered.TrySetResult();
                ReleaseCallback.Task.GetAwaiter().GetResult();
            });
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return request.Value;
        }
    }

    private sealed class ImmediateWorker(BackgroundTaskKind kind) : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => kind;
        protected override ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken) => ValueTask.FromResult(request.Value);
    }

    private sealed class ContextWorker : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.LibraryRefresh;
        public SynchronizationContext? ExecutionContext { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
        {
            ExecutionContext = SynchronizationContext.Current;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return request.Value;
        }
    }

    private sealed class MarkerSynchronizationContext : SynchronizationContext;

    private sealed class NullDiagnostics : IOperationDiagnostics
    {
        public IDisposable Begin(string operation, string? subject = null) => new Scope();
        public void Record(string operation, string? subject = null, string? detail = null) { }
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }

    private sealed class RecordingDiagnostics : IOperationDiagnostics
    {
        private readonly List<(string Operation, string? Subject)> events = [];
        public IDisposable Begin(string operation, string? subject = null) => new Scope();
        public void Record(string operation, string? subject = null, string? detail = null) => events.Add((operation, subject));
        public IReadOnlyList<string> EventsFor(string subject) => events.Where(entry => entry.Subject == subject).Select(entry => entry.Operation).ToArray();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
