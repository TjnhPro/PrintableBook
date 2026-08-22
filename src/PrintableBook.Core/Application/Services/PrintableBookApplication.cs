using PrintableBook.Core.Application.Commands;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Progress;
using PrintableBook.Core.Application.Results;

namespace PrintableBook.Core.Application.Services;

public sealed class PrintableBookApplication(IBookProcessingPipeline pipeline) : IPrintableBookApplication
{
    public ValueTask<ProcessingResult> ProcessAsync(
        ProcessingRequest request,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return pipeline.ProcessAsync(request, progress, cancellationToken);
    }
}
