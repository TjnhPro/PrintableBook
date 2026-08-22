using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record InteriorPagePipelineRequest(
    BookWorkspace Workspace,
    FileReference Source,
    string PageId,
    ArtworkDetectionThreshold ArtworkDetectionThreshold,
    ImageSize TargetSize,
    ImageDensity TargetDensity,
    FileReference? Frame,
    bool IsFrameEnabled);

public sealed record InteriorPageProcessingResult(string PageId, FileReference Source, FileReference FinalPage);

public sealed class InteriorPageProcessingException(
    string pageId,
    string step,
    Exception innerException) : Exception($"Interior page '{pageId}' failed during {step}.", innerException)
{
    public string PageId { get; } = pageId;

    public string Step { get; } = step;
}

/// <summary>
/// Runs sequential image stages for one interior page. Parallelism is owned by the batch processor.
/// </summary>
public interface IInteriorPagePipeline
{
    ValueTask<InteriorPageProcessingResult> ProcessAsync(
        InteriorPagePipelineRequest request,
        CancellationToken cancellationToken = default);
}
