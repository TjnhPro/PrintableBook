using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record ProcessQueueEntry(BookId BookId, BookProcessingStatus Status, string? Detail);
public sealed record ProcessSessionSnapshot(bool IsActive, bool IsCancelling, string? BrandName, BookId? CurrentBookId, string? CurrentStep, IReadOnlyList<ProcessQueueEntry> Queue);

public interface IProcessSessionService
{
    ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default);
    ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, CancellationToken cancellationToken = default);
    ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default);
}

public sealed class ProcessSessionService(
    IApplicationSnapshotService snapshotService,
    IPrintableBookApplication application,
    IFileSystem fileSystem) : IProcessSessionService
{
    private readonly Lock sync = new();
    private ProcessSessionSnapshot snapshot = new(false, false, null, null, null, []);
    private CancellationTokenSource? cancellation;

    public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync) return ValueTask.FromResult(snapshot);
    }

    public async ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookIds);
        if (bookIds.Count == 0) throw new ArgumentException("Select at least one Book before starting processing.", nameof(bookIds));

        var applicationSnapshot = await snapshotService.RefreshAsync(cancellationToken);
        var selected = applicationSnapshot.Discovery.Books
            .Where(book => bookIds.Contains(book.Id.Value, StringComparer.Ordinal))
            .ToArray();
        if (selected.Length != bookIds.Count) throw new ArgumentException("One or more selected Books no longer exist.", nameof(bookIds));

        var summaries = applicationSnapshot.BookSummaries.ToDictionary(summary => summary.BookId.Value, StringComparer.Ordinal);
        if (selected.Any(book => !string.Equals(summaries[book.Id.Value].ValidationStatus, "Ready", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Every selected Book must be validation-ready before processing.");
        }

        lock (sync)
        {
            if (snapshot.IsActive) return snapshot;
            cancellation = new CancellationTokenSource();
            snapshot = new ProcessSessionSnapshot(true, false, brandName, selected[0].Id, "Preparing", selected.Select((book, index) => new ProcessQueueEntry(book.Id, index == 0 ? BookProcessingStatus.Running : BookProcessingStatus.NotStarted, index == 0 ? "Preparing" : "Waiting")).ToArray());
        }

        _ = ExecuteAsync(applicationSnapshot, selected, brandName, cancellation.Token);
        return await GetAsync(cancellationToken);
    }

    public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!snapshot.IsActive || cancellation is null) return ValueTask.FromResult(snapshot);
            snapshot = snapshot with { IsCancelling = true, CurrentStep = "Cancelling" };
            cancellation.Cancel();
            return ValueTask.FromResult(snapshot);
        }
    }

    private async Task ExecuteAsync(ApplicationSnapshot applicationSnapshot, IReadOnlyList<DiscoveredBook> books, string? brandName, CancellationToken cancellationToken)
    {
        try
        {
            FileReference? frame = null;
            if (!string.IsNullOrWhiteSpace(brandName))
            {
                var brand = applicationSnapshot.Discovery.Brands.FirstOrDefault(item => string.Equals(item.Name, brandName, StringComparison.Ordinal));
                if (brand is not null)
                {
                    var candidate = new FileReference(Path.Combine(brand.Directory.Value, "frame.png"));
                    if (await fileSystem.FileExistsAsync(candidate, cancellationToken)) frame = candidate;
                }
            }

            var settings = applicationSnapshot.GlobalSettings;
            var request = new BookProcessingQueueRequest(books.Select(book => new PrintableBookProcessingCommand(
                book.Id,
                book.Directory,
                new DirectoryReference(Path.Combine(applicationSnapshot.Discovery.Paths.Root.Value, "outputs")),
                new ImageSize(1, 1),
                new ImageSize(settings.FinalPageWidth, settings.FinalPageHeight),
                new ImageDensity(settings.Dpi, settings.Dpi),
                new PhysicalPageSize(settings.InteriorPdfWidthInches, settings.InteriorPdfHeightInches),
                new PhysicalPageSize(settings.InteriorPdfWidthInches, settings.InteriorPdfHeightInches),
                settings.MaximumPageConcurrency,
                new ArtworkDetectionThreshold(settings.ArtworkDetectionThreshold),
                frame,
                frame is not null,
                null)).ToArray());

            lock (sync) snapshot = snapshot with { CurrentStep = "Processing" };
            var result = await application.ProcessBooksAsync(request, cancellationToken);
            lock (sync)
            {
                snapshot = new ProcessSessionSnapshot(false, false, brandName, null, null,
                    result.Books.Select(book => new ProcessQueueEntry(book.BookId, book.Status, book.Failure?.Message)).ToArray());
                cancellation?.Dispose();
                cancellation = null;
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
                cancellation?.Dispose();
                cancellation = null;
            }
        }
        catch (Exception exception)
        {
            lock (sync)
            {
                snapshot = snapshot with { IsActive = false, IsCancelling = false, CurrentStep = "Failed", Queue = snapshot.Queue.Select(entry => entry.Status == BookProcessingStatus.NotStarted ? entry with { Status = BookProcessingStatus.Failed, Detail = exception.Message } : entry).ToArray() };
                cancellation?.Dispose();
                cancellation = null;
            }
        }
    }
}
