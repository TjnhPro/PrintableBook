using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Desktop.Preview;

namespace PrintableBook.Desktop.Tests;

public sealed class BookAssetPreviewCoordinatorTests
{
    [Fact]
    public async Task Same_asset_uses_the_same_opaque_manager_key_and_different_assets_are_distinct()
    {
        var manager = new RecordingManager();
        var coordinator = new BookAssetPreviewCoordinator(manager);

        var first = await coordinator.StartAsync("Book", "interior/one.png");
        var duplicate = await coordinator.StartAsync("Book", "interior/one.png");
        var distinct = await coordinator.StartAsync("Book", "interior/two.png");

        Assert.Equal(first.TaskId, duplicate.TaskId);
        Assert.NotEqual(first.TaskId, distinct.TaskId);
        Assert.All(manager.Keys, key => Assert.DoesNotContain("interior/", key));
    }

    [Fact]
    public async Task Result_is_returned_only_for_an_asset_preview_task()
    {
        var manager = new RecordingManager();
        var coordinator = new BookAssetPreviewCoordinator(manager);
        var task = await coordinator.StartAsync("Book", "interior/one.png");
        var preview = new BookAssetPreview("Book", "interior/one.png", 100, 100, "data:image/png;base64,test");
        manager.SetResult(task.TaskId, preview);

        Assert.True(coordinator.TryGetResult(task.TaskId, out BookAssetPreview? result));
        Assert.Same(preview, result);
    }

    private sealed class RecordingManager : IBackgroundTaskManager
    {
        private readonly Dictionary<string, BackgroundTaskSnapshot> tasks = [];
        private readonly Dictionary<BackgroundTaskId, object?> results = [];
        public IReadOnlyCollection<string> Keys => tasks.Keys;
        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default)
        {
            if (!tasks.TryGetValue(key, out var task)) tasks[key] = task = new(new BackgroundTaskId($"task-{tasks.Count}"), kind, BackgroundTaskState.Queued, key, subject, null, null, null, null, null, null, null, null);
            return ValueTask.FromResult(task);
        }
        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(tasks.Values.SingleOrDefault(task => task.TaskId == taskId));
        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>(tasks.Values.ToArray());
        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => GetAsync(taskId, cancellationToken);
        public ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
        {
            if (results.TryGetValue(taskId, out var value) && value is TResult typed) { result = typed; return true; }
            result = default;
            return false;
        }
        public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? view) where TView : class { view = null; return false; }
        public void SetResult(BackgroundTaskId taskId, object? result) => results[taskId] = result;
    }
}
