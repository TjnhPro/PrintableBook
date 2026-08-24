using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record ProcessQueueEntry(BookId BookId, BookProcessingStatus Status, string? Detail);
public sealed record ProcessSessionSnapshot(bool IsActive, bool IsCancelling, string? BrandName, BookId? CurrentBookId, string? CurrentStep, IReadOnlyList<ProcessQueueEntry> Queue, int PagesCompleted = 0, int PagesTotal = 0, int WorkerLimit = 0);

public interface IProcessSessionService
{
    ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default);
    ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default);
    ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class ProcessSessionService(
    IApplicationSnapshotService snapshotService,
    IPrintableBookApplication application,
    IBrandFrameResolver brandFrameResolver) : IProcessSessionService
{
    private readonly Lock sync = new();
    private ProcessSessionSnapshot snapshot = new(false, false, null, null, null, []);
    private CancellationTokenSource? cancellation;
    private Task? executionTask;

    public async ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessSessionSnapshot current;
        lock (sync) current = snapshot;
        if (!current.IsActive) return current;
        var refreshed = await snapshotService.RefreshAsync(cancellationToken);
        lock (sync)
        {
            if (!snapshot.IsActive) return snapshot;
            var summaries = refreshed.BookSummaries.ToDictionary(item => item.BookId);
            var queue = snapshot.Queue.Select(entry =>
            {
                if (!summaries.TryGetValue(entry.BookId, out var summary) || summary.WorkspaceStatus == BookProcessingStatus.NotStarted)
                {
                    return entry;
                }

                return entry with { Status = summary.WorkspaceStatus, Detail = summary.CurrentStep ?? entry.Detail };
            }).ToArray();
            var active = refreshed.BookSummaries.FirstOrDefault(summary =>
                summary.WorkspaceStatus == BookProcessingStatus.Running &&
                queue.Any(entry => entry.BookId == summary.BookId));
            snapshot = snapshot with
            {
                Queue = queue,
                CurrentBookId = active?.BookId ?? snapshot.CurrentBookId,
                CurrentStep = active?.CurrentStep ?? snapshot.CurrentStep,
                PagesCompleted = active?.InteriorPages.Count ?? snapshot.PagesCompleted,
                PagesTotal = active?.InteriorSourcePageCount ?? snapshot.PagesTotal,
                WorkerLimit = refreshed.GlobalSettings.MaximumPageConcurrency
            };
            return snapshot;
        }
    }

    public async ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookIds);
        if (bookIds.Count == 0) throw new ArgumentException("Select at least one Book before starting processing.", nameof(bookIds));

        if (string.IsNullOrWhiteSpace(brandName)) throw new ArgumentException("Select one Brand before starting processing.", nameof(brandName));
        var applicationSnapshot = await snapshotService.RefreshAsync(cancellationToken);
        if (!applicationSnapshot.Discovery.Brands.Any(brand => string.Equals(brand.Name, brandName, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The selected Brand no longer exists.", nameof(brandName));
        }
        var selected = applicationSnapshot.Discovery.Books
            .Where(book => bookIds.Contains(book.Id.Value, StringComparer.Ordinal))
            .ToArray();
        if (selected.Length != bookIds.Count) throw new ArgumentException("One or more selected Books no longer exist.", nameof(bookIds));

        var summaries = applicationSnapshot.BookSummaries.ToDictionary(summary => summary.BookId.Value, StringComparer.Ordinal);
        if (selected.Any(book => !string.Equals(summaries[book.Id.Value].ValidationStatus, "Ready", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Every selected Book must be validation-ready before processing.");
        }

        CancellationTokenSource sessionCancellation;
        ProcessSessionSnapshot started;
        lock (sync)
        {
            if (snapshot.IsActive || executionTask is { IsCompleted: false }) return snapshot;
            sessionCancellation = new CancellationTokenSource();
            cancellation = sessionCancellation;
            snapshot = new ProcessSessionSnapshot(true, false, brandName, selected[0].Id, "Preparing", selected.Select((book, index) => new ProcessQueueEntry(book.Id, index == 0 ? BookProcessingStatus.Running : BookProcessingStatus.NotStarted, index == 0 ? "Preparing" : "Waiting")).ToArray(), 0, 0, applicationSnapshot.GlobalSettings.MaximumPageConcurrency);
            started = snapshot;
            executionTask = Task.Run(
                () => ExecuteAsync(applicationSnapshot, selected, brandName, mode, sessionCancellation.Token),
                CancellationToken.None);
        }

        return started;
    }

    public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource? toCancel;
        ProcessSessionSnapshot current;
        lock (sync)
        {
            if (!snapshot.IsActive || cancellation is null) return ValueTask.FromResult(snapshot);
            snapshot = snapshot with { IsCancelling = true, CurrentStep = "Cancelling" };
            toCancel = cancellation;
            current = snapshot;
        }

        RequestCancellation(toCancel);
        return ValueTask.FromResult(current);
    }

    public async ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        Task? task;
        CancellationTokenSource? toCancel;
        lock (sync)
        {
            task = executionTask;
            if (task is null) return true;
            if (snapshot.IsActive)
            {
                snapshot = snapshot with { IsCancelling = true, CurrentStep = "Cancelling" };
            }

            toCancel = cancellation;
        }

        RequestCancellation(toCancel);
        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task ExecuteAsync(ApplicationSnapshot applicationSnapshot, IReadOnlyList<DiscoveredBook> books, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken)
    {
        try
        {
            var summaries = applicationSnapshot.BookSummaries.ToDictionary(summary => summary.BookId.Value, StringComparer.Ordinal);
            FileReference? frame = null;
            if (!string.IsNullOrWhiteSpace(brandName))
            {
                var brand = applicationSnapshot.Discovery.Brands.FirstOrDefault(item => string.Equals(item.Name, brandName, StringComparison.Ordinal));
                if (brand is not null)
                {
                    frame = await brandFrameResolver.ResolveCompatibleFrameAsync(
                        brand,
                        new ImageSize(applicationSnapshot.GlobalSettings.ArtworkMaximumSide, applicationSnapshot.GlobalSettings.ArtworkMaximumSide),
                        cancellationToken);
                }
            }

            var settings = applicationSnapshot.GlobalSettings;
            var request = new BookProcessingQueueRequest(books.Select(book => new PrintableBookProcessingCommand(
                book.Id,
                book.Directory,
                new DirectoryReference(Path.Combine(applicationSnapshot.Discovery.Paths.Root.Value, "outputs")),
                new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide),
                new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide),
                new ImageSize(settings.WorkingPageWidth, settings.WorkingPageHeight),
                new ImageSize(settings.FinalPageWidth, settings.FinalPageHeight),
                new ImageDensity(settings.Dpi, settings.Dpi),
                new PhysicalPageSize(settings.InteriorPdfWidthInches, settings.InteriorPdfHeightInches),
                new PhysicalPageSize(settings.InteriorPdfWidthInches, settings.InteriorPdfHeightInches),
                settings.MaximumPageConcurrency,
                new ArtworkDetectionThreshold(settings.ArtworkDetectionThreshold),
                frame,
                null,
                string.IsNullOrWhiteSpace(summaries[book.Id.Value].SelectedCoverReference) ? null : new FileReference(summaries[book.Id.Value].SelectedCoverReference!),
                mode)).ToArray());

            lock (sync) snapshot = snapshot with { CurrentStep = "Processing" };
            var result = await application.ProcessBooksAsync(request, cancellationToken);
            lock (sync)
            {
                snapshot = new ProcessSessionSnapshot(false, false, brandName, null, null,
                    result.Books.Select(book => new ProcessQueueEntry(book.BookId, book.Status, book.Failure?.Message)).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            lock (sync)
            {
                snapshot = snapshot with
                {
                    IsActive = false,
                    IsCancelling = false,
                    CurrentStep = "Cancelled",
                    Queue = snapshot.Queue.Select(entry => entry.Status == BookProcessingStatus.Running
                        ? entry with { Status = BookProcessingStatus.Cancelled, Detail = "Cancelled" }
                        : entry).ToArray()
                };
            }
        }
        catch (Exception exception)
        {
            lock (sync)
            {
                snapshot = snapshot with { IsActive = false, IsCancelling = false, CurrentStep = "Failed", Queue = snapshot.Queue.Select(entry => entry.Status == BookProcessingStatus.Running ? entry with { Status = BookProcessingStatus.Failed, Detail = exception.Message } : entry).ToArray() };
            }
        }
        finally
        {
            lock (sync)
            {
                cancellation?.Dispose();
                cancellation = null;
                executionTask = null;
            }
        }
    }

    private static void RequestCancellation(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Terminal cleanup won the race; there is nothing left to cancel.
        }
    }

}
