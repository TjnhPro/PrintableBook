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
        await Task.WhenAll(library.Started.Task, processing.Started.Task, preview.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)));

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

    private static BackgroundTaskManager CreateManager(params IBackgroundTaskWorker[] workers)
    {
        var services = new ServiceCollection();
        foreach (var worker in workers) services.AddKeyedSingleton<IBackgroundTaskWorker>(worker.Kind, worker);
        return new BackgroundTaskManager(services.BuildServiceProvider(), new NullDiagnostics());
    }

    private sealed record TaskRequest(string Value);
    private sealed record TaskView(string Value);

    private sealed class BlockingWorker(BackgroundTaskKind kind) : BackgroundTaskWorker<TaskRequest, string>
    {
        private int active;
        public override BackgroundTaskKind Kind => kind;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            if (StartedRequests.Count >= 3) StartedThree.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return request.Value;
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }

    private sealed class NullDiagnostics : IOperationDiagnostics
    {
        public IDisposable Begin(string operation, string? subject = null) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
