using PrintableBook.Core.Application.Commands;
using PrintableBook.Core.Application.Progress;
using PrintableBook.Core.Application.Results;

namespace PrintableBook.Core.Application.Pipelines;

/// <summary>
/// Runs registered stages in order. Pipeline policy remains intentionally minimal in Phase 1.
/// </summary>
public sealed class BookProcessingPipeline(IEnumerable<IBookProcessingStage> stages) : IBookProcessingPipeline
{
    private readonly IReadOnlyList<IBookProcessingStage> stages = stages?.ToArray()
        ?? throw new ArgumentNullException(nameof(stages));

    public async ValueTask<ProcessingResult> ProcessAsync(
        ProcessingRequest request,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var terminalResult = await stage.ProcessAsync(request.Context, progress, cancellationToken);

            if (terminalResult is not null)
            {
                return terminalResult;
            }
        }

        return ProcessingResult.Succeeded();
    }
}
