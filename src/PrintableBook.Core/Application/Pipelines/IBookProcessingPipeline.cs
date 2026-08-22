using PrintableBook.Core.Application.Commands;
using PrintableBook.Core.Application.Progress;
using PrintableBook.Core.Application.Results;

namespace PrintableBook.Core.Application.Pipelines;

public interface IBookProcessingPipeline
{
    ValueTask<ProcessingResult> ProcessAsync(
        ProcessingRequest request,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
