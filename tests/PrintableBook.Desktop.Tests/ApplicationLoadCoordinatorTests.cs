using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Desktop.Loading;

namespace PrintableBook.Desktop.Tests;

public sealed class ApplicationLoadCoordinatorTests
{
    [Fact]
    public async Task StartRefreshAsync_returns_the_manager_owned_task_without_waiting_for_worker_completion()
    {
        var manager = new RecordingTaskManager(CreateSnapshot(), completed: false);
        var coordinator = new ApplicationLoadCoordinator(manager);

        var task = await coordinator.StartRefreshAsync();

        Assert.Equal(BackgroundTaskKind.LibraryRefresh, task.Kind);
        Assert.Equal("library", task.Key);
        Assert.Equal(1, manager.StartCount);
    }

    [Fact]
    public async Task Provider_waits_for_the_manager_result_and_returns_the_snapshot()
    {
        var expected = CreateSnapshot();
        var coordinator = new ApplicationLoadCoordinator(new RecordingTaskManager(expected));

        var actual = await coordinator.GetFreshAsync();

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Provider_caller_cancellation_cancels_only_its_wait()
    {
        using var cancellation = new CancellationTokenSource();
        var manager = new RecordingTaskManager(CreateSnapshot(), completed: false);
        var coordinator = new ApplicationLoadCoordinator(manager);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.GetFreshAsync(cancellation.Token).AsTask());
        Assert.Equal(0, manager.CancellationCount);
    }

    private static ApplicationSnapshot CreateSnapshot() => new(
        new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], []),
        GlobalSettings.Default, [], DateTimeOffset.UtcNow);

    private sealed class RecordingTaskManager(ApplicationSnapshot snapshot, bool completed = true) : IBackgroundTaskManager
    {
        private readonly BackgroundTaskId id = new("task-library");
        public int StartCount { get; private set; }
        public int CancellationCount { get; private set; }
        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return ValueTask.FromResult(Snapshot(completed ? BackgroundTaskState.Completed : BackgroundTaskState.Running));
        }
        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Snapshot(completed ? BackgroundTaskState.Completed : BackgroundTaskState.Running));
        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>([Snapshot(BackgroundTaskState.Completed)]);
        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) { CancellationCount++; return ValueTask.FromResult<BackgroundTaskSnapshot?>(Snapshot(BackgroundTaskState.Cancelled)); }
        public async ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) { if (!completed) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return true; }
        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
        {
            if (snapshot is TResult typed) { result = typed; return true; }
            result = default;
            return false;
        }
        public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? view) where TView : class { view = null; return false; }
        private BackgroundTaskSnapshot Snapshot(BackgroundTaskState state) => new(id, BackgroundTaskKind.LibraryRefresh, state, "library", "Library", null, null, null, null, DateTimeOffset.UtcNow, state == BackgroundTaskState.Completed ? DateTimeOffset.UtcNow : null, null, null);
    }
}
