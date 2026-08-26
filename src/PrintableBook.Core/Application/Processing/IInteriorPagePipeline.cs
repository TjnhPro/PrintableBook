using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record InteriorPagePipelineRequest
{
    public InteriorPagePipelineRequest(
        BookWorkspace workspace,
        FileReference source,
        string pageId,
        ArtworkDetectionThreshold artworkDetectionThreshold,
        ImageSize preparedArtworkSize,
        ImageSize workingPageSize,
        ImageSize finalPageSize,
        ImageDensity targetDensity,
        FileReference? frame,
        FrameMode frameMode,
        ArtworkSourceNormalizationSettings? artworkSourceNormalization = null,
        BorderLineDetectionSettings? borderLineDetection = null)
    {
        Workspace = workspace;
        Source = source;
        PageId = pageId;
        ArtworkDetectionThreshold = artworkDetectionThreshold;
        PreparedArtworkSize = preparedArtworkSize;
        WorkingPageSize = workingPageSize;
        FinalPageSize = finalPageSize;
        TargetDensity = targetDensity;
        Frame = frame;
        FrameMode = frameMode;
        ArtworkSourceNormalization = artworkSourceNormalization ?? ArtworkSourceNormalizationSettings.Default;
        BorderLineDetection = borderLineDetection ?? BorderLineDetectionSettings.Default;
        ValidateGeometry();
    }

    public BookWorkspace Workspace { get; init; }
    public FileReference Source { get; init; }
    public string PageId { get; init; }
    public ArtworkDetectionThreshold ArtworkDetectionThreshold { get; init; }
    public ImageSize PreparedArtworkSize { get; init; }
    public ImageSize WorkingPageSize { get; init; }
    public ImageSize FinalPageSize { get; init; }
    public ImageDensity TargetDensity { get; init; }
    public FileReference? Frame { get; init; }
    public FrameMode FrameMode { get; init; }
    public ArtworkSourceNormalizationSettings ArtworkSourceNormalization { get; init; }
    public BorderLineDetectionSettings BorderLineDetection { get; init; }

    public void ValidateGeometry()
    {
        if (WorkingPageSize.Width < PreparedArtworkSize.Width || WorkingPageSize.Height < PreparedArtworkSize.Height)
        {
            throw new ArgumentException("The working page must contain the prepared artwork.", nameof(WorkingPageSize));
        }

        if (FinalPageSize.Width < WorkingPageSize.Width || FinalPageSize.Height < WorkingPageSize.Height)
        {
            throw new ArgumentException("The final page must contain the working page.", nameof(FinalPageSize));
        }
    }
}

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
