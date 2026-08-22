using PrintableBook.Core.Application.Commands;
using PrintableBook.Core.Application.Progress;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Results;

namespace PrintableBook.Core.Application.Services;

/// <summary>
/// Application-level processing entry point used by presentation hosts.
/// </summary>
public interface IPrintableBookApplication
{
    ValueTask<ProcessingResult> ProcessAsync(
        ProcessingRequest request,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    ValueTask<BookProcessingQueueResult> ProcessBooksAsync(
        BookProcessingQueueRequest request,
        CancellationToken cancellationToken = default);
}
