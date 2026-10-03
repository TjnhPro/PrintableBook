namespace PrintableBook.Core.Application.BackgroundTasks;

public interface IBackgroundTaskManager
{
    async ValueTask<BackgroundTaskStartResult> StartWithStatusAsync<TRequest>(
        BackgroundTaskKind kind,
        string key,
        string? subject,
        TRequest request,
        object? initialView = null,
        CancellationToken cancellationToken = default) =>
        new(await StartAsync(kind, key, subject, request, initialView, cancellationToken), true);

    ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(
        BackgroundTaskKind kind,
        string key,
        string? subject,
        TRequest request,
        object? initialView = null,
        CancellationToken cancellationToken = default);

    ValueTask<BackgroundTaskSnapshot?> GetAsync(
        BackgroundTaskId taskId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(
        BackgroundTaskKind? kind = null,
        CancellationToken cancellationToken = default);

    ValueTask<BackgroundTaskSnapshot?> CancelAsync(
        BackgroundTaskId taskId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> WaitAsync(
        BackgroundTaskId taskId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    bool TryGetResult<TResult>(
        BackgroundTaskId taskId,
        out TResult? result);

    bool TryGetView<TView>(
        BackgroundTaskId taskId,
        out TView? view)
        where TView : class;
}

public sealed record BackgroundTaskStartResult(BackgroundTaskSnapshot Snapshot, bool WasCreated);
