using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Core.Application.Desktop;

public sealed record AmazonAsinCrawlSessionSnapshot(
    string? TaskId,
    string? BookId,
    bool IsActive,
    bool IsCancelling,
    AmazonAsinCrawlView? View,
    string? ErrorCode = null,
    string? SourceFingerprint = null,
    string? ReceiptDigest = null);

public interface IAmazonAsinCrawlSessionService
{
    ValueTask<AmazonAsinCrawlSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default);
    ValueTask<AmazonAsinCrawlSessionSnapshot> StartAsync(string bookId, IReadOnlyList<string> keywords, CancellationToken cancellationToken = default);
    ValueTask<AmazonAsinCrawlSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default);
    ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class AmazonAsinCrawlSessionService(IBackgroundTaskManager taskManager) : IAmazonAsinCrawlSessionService
{
    public async ValueTask<AmazonAsinCrawlSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.AmazonAsinCrawl, cancellationToken);
        var task = tasks.FirstOrDefault(item => string.Equals(item.Subject, bookId, StringComparison.Ordinal) && IsActive(item.State))
            ?? tasks.FirstOrDefault(item => string.Equals(item.Subject, bookId, StringComparison.Ordinal));
        if (task is null)
        {
            var other = tasks.FirstOrDefault(item => IsActive(item.State));
            return new(other?.TaskId.Value, other?.Subject, other is not null, other?.State == BackgroundTaskState.Cancelling, null,
                other is null ? null : "amazon_asin_crawl_active");
        }
        return Snapshot(task);
    }

    public async ValueTask<AmazonAsinCrawlSessionSnapshot> StartAsync(string bookId, IReadOnlyList<string> keywords, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var request = AmazonAsinCrawlRequest.Create(keywords);
        var initial = AmazonAsinCrawlView.Pending(request);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.AmazonAsinCrawl,
            $"{bookId}:{request.RequestFingerprint}",
            bookId,
            request,
            initial,
            cancellationToken);
        return Snapshot(task, initial);
    }

    public async ValueTask<AmazonAsinCrawlSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.AmazonAsinCrawl, cancellationToken);
        var task = tasks.FirstOrDefault(item => string.Equals(item.Subject, bookId, StringComparison.Ordinal) && IsActive(item.State));
        if (task is null) return await GetAsync(bookId, cancellationToken);
        var cancelled = await taskManager.CancelAsync(task.TaskId, cancellationToken);
        return cancelled is null ? await GetAsync(bookId, cancellationToken) : Snapshot(cancelled);
    }

    public async ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.AmazonAsinCrawl, cancellationToken);
        var task = tasks.FirstOrDefault(item => IsActive(item.State));
        if (task is null) return true;
        await taskManager.CancelAsync(task.TaskId, cancellationToken);
        return await taskManager.WaitAsync(task.TaskId, timeout, cancellationToken);
    }

    private AmazonAsinCrawlSessionSnapshot Snapshot(BackgroundTaskSnapshot task, AmazonAsinCrawlView? fallback = null)
    {
        taskManager.TryGetView(task.TaskId, out AmazonAsinCrawlView? view);
        return new(task.TaskId.Value, task.Subject, IsActive(task.State), task.State == BackgroundTaskState.Cancelling, view ?? fallback, task.ErrorCode);
    }

    private static bool IsActive(BackgroundTaskState state) => state is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling;
}
