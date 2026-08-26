using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Processing;

public sealed record PrintableBookProcessingCommand(
    BookId BookId,
    DirectoryReference BookDirectory,
    DirectoryReference FinalOutputRoot,
    ImageSize MinimumCoverSize,
    ImageSize PreparedArtworkSize,
    ImageSize WorkingPageSize,
    ImageSize FinalPageSize,
    ImageDensity TargetInteriorDensity,
    PhysicalPageSize CoverPdfPageSize,
    PhysicalPageSize InteriorPdfPageSize,
    int MaximumPageConcurrency,
    ArtworkDetectionThreshold ArtworkDetectionThreshold,
    FileReference? Frame,
    int? ShuffleSeed,
    FileReference? SelectedCover = null,
    BookProcessingMode Mode = BookProcessingMode.FullBook,
    FileReference? BackgroundPage = null,
    ArtworkSourceNormalizationSettings? ArtworkSourceNormalization = null,
    BorderLineDetectionSettings? BorderLineDetection = null,
    IReadOnlyList<FileReference>? IntroTemplatePages = null)
{
    public ArtworkSourceNormalizationSettings EffectiveArtworkSourceNormalization => ArtworkSourceNormalization ?? ArtworkSourceNormalizationSettings.Default;
    public BorderLineDetectionSettings EffectiveBorderLineDetection => BorderLineDetection ?? BorderLineDetectionSettings.Default;
    public IReadOnlyList<FileReference> EffectiveIntroTemplatePages => IntroTemplatePages ?? [];
}

public sealed record BookProcessingQueueRequest(IReadOnlyList<PrintableBookProcessingCommand> Books);

public sealed record BookProcessingQueueBookResult(
    BookId BookId,
    BookProcessingStatus Status,
    ProcessingFailure? Failure,
    PublishedBookOutputs? PublishedOutputs,
    PublishedInteriorOutput? PublishedInteriorOutput = null)
{
    public static BookProcessingQueueBookResult Completed(BookId bookId, PublishedBookOutputs? outputs) =>
        new(bookId, BookProcessingStatus.Completed, null, outputs);

    public static BookProcessingQueueBookResult CompletedInterior(BookId bookId, PublishedInteriorOutput output) =>
        new(bookId, BookProcessingStatus.Completed, null, null, output);
}

public sealed record BookProcessingQueueResult(bool IsAlreadyRunning, IReadOnlyList<BookProcessingQueueBookResult> Books)
{
    public static BookProcessingQueueResult AlreadyRunning() => new(true, []);
}

public interface IBookProcessingQueueBookProcessor
{
    ValueTask<BookProcessingQueueBookResult> ProcessBookAsync(
        PrintableBookProcessingCommand command,
        Action<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the session gate for a whole queue and deliberately processes books one at a time.
/// </summary>
public sealed class BookProcessingQueueProcessor(
    IProcessingSessionGate sessionGate,
    IBookProcessingQueueBookProcessor bookProcessor)
{
    public async ValueTask<BookProcessingQueueResult> ProcessAsync(
        BookProcessingQueueRequest request,
        Action<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Books.Count == 0)
        {
            return new BookProcessingQueueResult(false, []);
        }

        await using var lease = await sessionGate.TryAcquireAsync(cancellationToken);
        if (lease is null)
        {
            return BookProcessingQueueResult.AlreadyRunning();
        }

        var results = new List<BookProcessingQueueBookResult>(request.Books.Count);
        foreach (var book in request.Books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(new BookProcessingProgress(book.BookId, BookProcessingStatus.Running, "Preparing"));
            var result = await bookProcessor.ProcessBookAsync(book, progress, cancellationToken);
            results.Add(result);
            progress?.Invoke(new BookProcessingProgress(result.BookId, result.Status, result.Status.ToString(), detail: result.Failure?.Message));
        }

        return new BookProcessingQueueResult(false, results);
    }
}
