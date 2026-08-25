using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Desktop.Loading;

public enum ApplicationLoadKind { Initial, Refresh }

public sealed class ApplicationLoadCoordinator(IBackgroundTaskManager taskManager) : IApplicationSnapshotProvider
{
    public ValueTask<BackgroundTaskSnapshot> StartRefreshAsync(CancellationToken cancellationToken = default) =>
        taskManager.StartAsync(
            BackgroundTaskKind.LibraryRefresh,
            key: "library",
            subject: "Library",
            request: new LibraryRefreshRequest(),
            cancellationToken: cancellationToken);

    public ValueTask<BackgroundTaskSnapshot?> GetTaskAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) =>
        taskManager.GetAsync(taskId, cancellationToken);

    public bool TryGetResult(BackgroundTaskId taskId, out ApplicationSnapshot? snapshot) =>
        taskManager.TryGetResult(taskId, out snapshot);

    public async ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default)
    {
        var task = await StartRefreshAsync(cancellationToken);
        var completed = await taskManager.WaitAsync(task.TaskId, Timeout.InfiniteTimeSpan, cancellationToken);
        if (!completed) throw new InvalidOperationException("Library refresh wait ended unexpectedly.");

        var current = await taskManager.GetAsync(task.TaskId, cancellationToken)
            ?? throw new InvalidOperationException("Library refresh task disappeared.");
        if (current.State == BackgroundTaskState.Cancelled) throw new OperationCanceledException("Library refresh was cancelled.");
        if (current.State == BackgroundTaskState.Failed) throw new InvalidOperationException(current.ErrorMessage ?? "Library refresh failed.");
        if (!taskManager.TryGetResult<ApplicationSnapshot>(task.TaskId, out var snapshot) || snapshot is null)
        {
            throw new InvalidOperationException("Library refresh completed without a snapshot.");
        }

        return snapshot;
    }

    public ValueTask<ApplicationSnapshot> RefreshAsync(ApplicationLoadKind kind, CancellationToken cancellationToken = default) => GetFreshAsync(cancellationToken);
}
