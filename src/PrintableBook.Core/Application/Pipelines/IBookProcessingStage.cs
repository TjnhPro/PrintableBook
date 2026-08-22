using PrintableBook.Core.Application.Commands;
using PrintableBook.Core.Application.Progress;
using PrintableBook.Core.Application.Results;

namespace PrintableBook.Core.Application.Pipelines;

/// <summary>
/// A replaceable processing step. Returning null allows the next stage to continue.
/// </summary>
public interface IBookProcessingStage
{
    string Name { get; }

    ValueTask<ProcessingResult?> ProcessAsync(
        ProcessingContext context,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
