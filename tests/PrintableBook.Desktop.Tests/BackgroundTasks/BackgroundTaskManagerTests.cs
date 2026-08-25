using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Desktop.BackgroundTasks;

namespace PrintableBook.Desktop.Tests.BackgroundTasks;

public sealed class BackgroundTaskManagerTests
{
    [Fact]
    public void Policies_define_only_library_and_processing_lanes()
    {
        Assert.Equal(
            [BackgroundTaskKind.LibraryRefresh, BackgroundTaskKind.ProcessingSession],
            BackgroundTaskPolicies.All.Keys.Order());
        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Library, 1, BackgroundTaskDuplicatePolicy.JoinByKind), BackgroundTaskPolicies.For(BackgroundTaskKind.LibraryRefresh));
        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Processing, 1, BackgroundTaskDuplicatePolicy.ReturnExisting), BackgroundTaskPolicies.For(BackgroundTaskKind.ProcessingSession));
    }

    [Fact]
    public async Task StartAsync_applies_the_two_locked_duplicate_policies_and_independent_lanes()
    {
        var library = new BlockingWorker(BackgroundTaskKind.LibraryRefresh);
        var processing = new BlockingWorker(BackgroundTaskKind.ProcessingSession);
        using var manager = CreateManager(library, processing);

        var libraryFirst = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "one", null, new TaskRequest("one"));
        await library.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var librarySecond = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "two", null, new TaskRequest("two"));
        var processingFirst = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "one", null, new TaskRequest("one"));
        await processing.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var processingSecond = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "two", null, new TaskRequest("two"));

        Assert.Equal(libraryFirst.TaskId, librarySecond.TaskId);
        Assert.Equal(processingFirst.TaskId, processingSecond.TaskId);
        Assert.Equal(1, library.MaximumActive);
        Assert.Equal(1, processing.MaximumActive);

        library.Release.TrySetResult();
        processing.Release.TrySetResult();
        Assert.True(await manager.WaitAsync(libraryFirst.TaskId, TimeSpan.FromSeconds(2)));
        Assert.True(await manager.WaitAsync(processingFirst.TaskId, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Completed_task_retains_its_view_and_typed_result()
    {
        using var manager = CreateManager(new ImmediateWorker(BackgroundTaskKind.LibraryRefresh), new ImmediateWorker(BackgroundTaskKind.ProcessingSession));

        var task = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", "Library", new TaskRequest("complete"), new TaskView("initial"));

        Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
        Assert.True(manager.TryGetView<TaskView>(task.TaskId, out var view));
        Assert.Equal("initial", view!.Value);
        Assert.True(manager.TryGetResult<string>(task.TaskId, out var result));
        Assert.Equal("complete", result);
    }

    [Fact]
    public async Task Worker_failures_are_sanitized_unless_the_worker_supplies_a_safe_failure()
    {
        using var unexpected = CreateManager(new ThrowingWorker(new InvalidOperationException("D:\\secret")), new ImmediateWorker(BackgroundTaskKind.ProcessingSession));
        var unexpectedTask = await unexpected.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
        Assert.True(await unexpected.WaitAsync(unexpectedTask.TaskId, TimeSpan.FromSeconds(2)));
        var unexpectedSnapshot = await unexpected.GetAsync(unexpectedTask.TaskId);
        Assert.Equal(("background_task_failed", "Background task failed."), (unexpectedSnapshot!.ErrorCode, unexpectedSnapshot.ErrorMessage));

        using var safe = CreateManager(new ThrowingWorker(new BackgroundTaskFailureException("refresh_failed", "Library cannot be read.")), new ImmediateWorker(BackgroundTaskKind.ProcessingSession));
        var safeTask = await safe.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
        Assert.True(await safe.WaitAsync(safeTask.TaskId, TimeSpan.FromSeconds(2)));
        var safeSnapshot = await safe.GetAsync(safeTask.TaskId);
        Assert.Equal(("refresh_failed", "Library cannot be read."), (safeSnapshot!.ErrorCode, safeSnapshot.ErrorMessage));
    }

    [Fact]
    public async Task Worker_execution_isolated_from_the_callers_synchronization_context()
    {
        var worker = new ContextWorker();
        using var manager = CreateManager(worker, new ImmediateWorker(BackgroundTaskKind.ProcessingSession));
        var previous = SynchronizationContext.Current;
        var marker = new MarkerSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(marker);
            var task = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
            await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotSame(marker, worker.ExecutionContext);
            worker.Release.TrySetResult();
            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            worker.Release.TrySetResult();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task Lifecycle_diagnostics_remain_ordered_for_retained_task_kinds()
    {
        var diagnostics = new RecordingDiagnostics();
        using var manager = CreateManager(diagnostics, new ImmediateWorker(BackgroundTaskKind.LibraryRefresh), new ImmediateWorker(BackgroundTaskKind.ProcessingSession));

        var library = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", "Library", new TaskRequest("library"));
        var processing = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "processing", "Processing", new TaskRequest("processing"));

        Assert.True(await manager.WaitAsync(library.TaskId, TimeSpan.FromSeconds(2)));
        Assert.True(await manager.WaitAsync(processing.TaskId, TimeSpan.FromSeconds(2)));
        Assert.Equal(["task.queued", "task.started", "task.completed"], diagnostics.EventsFor("Library"));
        Assert.Equal(["task.queued", "task.started", "task.completed"], diagnostics.EventsFor("Processing"));
    }

    [Fact]
    public async Task Cancellation_and_disposal_do_not_block_on_a_worker_callback()
    {
        var worker = new BlockingCancellationWorker();
        var manager = CreateManager(worker, new ImmediateWorker(BackgroundTaskKind.ProcessingSession));
        try
        {
            var task = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, "library", null, new TaskRequest("library"));
            await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var cancel = manager.CancelAsync(task.TaskId).AsTask();
            await worker.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(cancel, await Task.WhenAny(cancel, Task.Delay(TimeSpan.FromSeconds(2))));
            Assert.Equal(BackgroundTaskState.Cancelling, (await cancel)!.State);

            var dispose = Task.Run(manager.Dispose);
            worker.ReleaseCallback.TrySetResult();
            await Task.WhenAll(cancel, dispose).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
            Assert.Equal(BackgroundTaskState.Cancelled, (await manager.GetAsync(task.TaskId))!.State);
        }
        finally
        {
            worker.ReleaseCallback.TrySetResult();
            manager.Dispose();
        }
    }

    [Fact]
    public async Task Completed_library_runs_are_retained_without_a_preview_history_volume_source()
    {
        using var manager = CreateManager(new ImmediateWorker(BackgroundTaskKind.LibraryRefresh), new ImmediateWorker(BackgroundTaskKind.ProcessingSession));

        var processing = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "processing", null, new TaskRequest("processing"));
        Assert.True(await manager.WaitAsync(processing.TaskId, TimeSpan.FromSeconds(2)));
        BackgroundTaskSnapshot? lastLibrary = null;
        for (var index = 0; index < 120; index++)
        {
            var task = await manager.StartAsync(BackgroundTaskKind.LibraryRefresh, $"library-{index}", null, new TaskRequest($"library-{index}"));
            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
            lastLibrary = task;
        }

        var history = await manager.ListAsync();
        Assert.InRange(history.Count, 2, 101);
        Assert.Contains(history, task => task.TaskId == processing.TaskId && task.State == BackgroundTaskState.Completed);
        Assert.Contains(history, task => task.TaskId == lastLibrary!.TaskId && task.State == BackgroundTaskState.Completed);
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
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumActive { get; private set; }

        protected override async ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
        {
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref active));
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return request.Value;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
    }

    private sealed class ImmediateWorker(BackgroundTaskKind kind) : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => kind;
        protected override ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken) => ValueTask.FromResult(request.Value);
    }

    private sealed class ThrowingWorker(Exception exception) : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.LibraryRefresh;
        protected override ValueTask<string> ExecuteTypedAsync(TaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken) => ValueTask.FromException<string>(exception);
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

    private sealed class ContextWorker : BackgroundTaskWorker<TaskRequest, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.LibraryRefresh;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SynchronizationContext? ExecutionContext { get; private set; }

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
