using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record ProcessQueueEntry(BookId BookId, BookProcessingStatus Status, string? Detail);
public sealed record ProcessSessionSnapshot(bool IsActive, bool IsCancelling, string? BrandName, BookId? CurrentBookId, string? CurrentStep, IReadOnlyList<ProcessQueueEntry> Queue, int PagesCompleted = 0, int PagesTotal = 0, int WorkerLimit = 0, DateTimeOffset? StartedAt = null);

public interface IProcessSessionService
{
    ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default);
    ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default);
    ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class ProcessSessionService(IBackgroundTaskManager taskManager) : IProcessSessionService
{
    private static readonly ProcessSessionSnapshot Idle = new(false, false, null, null, null, []);

    public async ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = await FindLatestAsync(cancellationToken);
        if (task is null) return Idle;
        if (!taskManager.TryGetView(task.TaskId, out ProcessSessionSnapshot? view) || view is null) return Overlay(Idle, task);
        return Overlay(view, task);
    }

    public async ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookIds);
        if (bookIds.Count == 0) throw new ArgumentException("Select at least one Book before starting processing.", nameof(bookIds));
        if (bookIds.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Book identifiers cannot be blank.", nameof(bookIds));
        if (bookIds.Distinct(StringComparer.Ordinal).Count() != bookIds.Count) throw new ArgumentException("Book identifiers must be distinct.", nameof(bookIds));
        if (string.IsNullOrWhiteSpace(brandName)) throw new ArgumentException("Select one Brand before starting processing.", nameof(brandName));

        var startedAt = DateTimeOffset.UtcNow;
        var ids = bookIds.ToArray();
        var initial = new ProcessSessionSnapshot(true, false, brandName, new BookId(ids[0]), "Queued", ids.Select((id, index) => new ProcessQueueEntry(new BookId(id), index == 0 ? BookProcessingStatus.Running : BookProcessingStatus.NotStarted, index == 0 ? "Queued" : "Waiting")).ToArray(), 0, 0, 0, startedAt);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.ProcessingSession,
            "processing",
            ids[0],
            new ProcessingSessionWorkerRequest(ids, brandName, mode, startedAt),
            initial,
            cancellationToken);
        return taskManager.TryGetView(task.TaskId, out ProcessSessionSnapshot? view) && view is not null ? Overlay(view, task) : Overlay(initial, task);
    }

    public async ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = await FindActiveAsync(cancellationToken);
        if (task is null) return await GetAsync(cancellationToken);
        await taskManager.CancelAsync(task.TaskId, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var task = await FindActiveAsync(cancellationToken);
        if (task is null) return true;
        await taskManager.CancelAsync(task.TaskId, cancellationToken);
        return await taskManager.WaitAsync(task.TaskId, timeout, cancellationToken);
    }

    private async ValueTask<BackgroundTaskSnapshot?> FindLatestAsync(CancellationToken cancellationToken)
    {
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.ProcessingSession, cancellationToken);
        return tasks.FirstOrDefault(task => task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling) ?? tasks.FirstOrDefault();
    }

    private async ValueTask<BackgroundTaskSnapshot?> FindActiveAsync(CancellationToken cancellationToken)
    {
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.ProcessingSession, cancellationToken);
        return tasks.FirstOrDefault(task => task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling);
    }

    private static ProcessSessionSnapshot Overlay(ProcessSessionSnapshot view, BackgroundTaskSnapshot task) => task.State switch
    {
        BackgroundTaskState.Queued or BackgroundTaskState.Running => view with { IsActive = true, IsCancelling = false },
        BackgroundTaskState.Cancelling => view with { IsActive = true, IsCancelling = true, CurrentStep = "Cancelling" },
        BackgroundTaskState.Cancelled => view with { IsActive = false, IsCancelling = false, CurrentStep = view.CurrentStep is "Cancelled" ? view.CurrentStep : "Cancelled", Queue = RewriteRunning(view.Queue, BookProcessingStatus.Cancelled, "Cancelled") },
        BackgroundTaskState.Failed => view with { IsActive = false, IsCancelling = false, CurrentStep = "Failed", Queue = RewriteRunning(view.Queue, BookProcessingStatus.Failed, task.ErrorMessage ?? "Processing failed.") },
        BackgroundTaskState.Completed => view with { IsActive = false, IsCancelling = false },
        _ => view
    };

    private static IReadOnlyList<ProcessQueueEntry> RewriteRunning(IReadOnlyList<ProcessQueueEntry> queue, BookProcessingStatus status, string detail) =>
        queue.Select(entry => entry.Status == BookProcessingStatus.Running ? entry with { Status = status, Detail = detail } : entry).ToArray();
}
