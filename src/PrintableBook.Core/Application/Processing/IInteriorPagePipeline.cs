using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public enum InteriorPageProcessingKind
{
    Interior = 0,
    IntroTemplate = 1,
    BrandIntroTemplate = 2,
    ProductionInterior = 3
}

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
        BorderLineDetectionSettings? borderLineDetection = null,
        InteriorPageProcessingKind processingKind = InteriorPageProcessingKind.Interior,
        string? outputFileName = null,
        string? frameContentSha256 = null)
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
        BorderLineDetection = borderLineDetection;
        ProcessingKind = processingKind;
        OutputFileName = outputFileName;
        FrameContentSha256 = frameContentSha256;
        if (!Enum.IsDefined(processingKind)) throw new ArgumentOutOfRangeException(nameof(processingKind), processingKind, "Unsupported page processing kind.");
        if (outputFileName is not null &&
            (string.IsNullOrWhiteSpace(outputFileName) ||
             !string.Equals(Path.GetFileName(outputFileName), outputFileName, StringComparison.Ordinal) ||
             !string.Equals(Path.GetExtension(outputFileName), ".png", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The processed output filename must be a plain PNG filename.", nameof(outputFileName));
        }
        ValidateProcessingPolicy();
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
    public BorderLineDetectionSettings? BorderLineDetection { get; init; }
    public InteriorPageProcessingKind ProcessingKind { get; init; }
    public string? OutputFileName { get; init; }
    public string? FrameContentSha256 { get; init; }

    public void ValidateProcessingPolicy()
    {
        if (!Enum.IsDefined(FrameMode))
        {
            throw new ArgumentOutOfRangeException(nameof(FrameMode), FrameMode, "Unsupported frame mode.");
        }
        if (ProcessingKind == InteriorPageProcessingKind.Interior && FrameMode == FrameMode.Enabled && Frame is null)
        {
            throw new ArgumentException("Frame Interior pages require a Brand frame.", nameof(Frame));
        }
        if (ProcessingKind is (InteriorPageProcessingKind.IntroTemplate or InteriorPageProcessingKind.BrandIntroTemplate) &&
            (Frame is not null || FrameMode != FrameMode.Disabled))
        {
            throw new ArgumentException("IntroTemplate pages must not apply a frame.", nameof(FrameMode));
        }
        if (ProcessingKind == InteriorPageProcessingKind.ProductionInterior &&
            (Frame is not null || FrameMode != FrameMode.Disabled))
        {
            throw new ArgumentException("Production Interior pages must use No Frame.", nameof(FrameMode));
        }
    }

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
    Exception innerException,
    InteriorPageProcessingKind processingKind = InteriorPageProcessingKind.Interior) : Exception($"Interior page '{pageId}' failed during {step}: {innerException.Message}", innerException)
{
    public string PageId { get; } = pageId;

    public string Step { get; } = step;

    public InteriorPageProcessingKind ProcessingKind { get; } = processingKind;

    public string? FailureCode { get; } = innerException is InteriorFrameException frameFailure
        ? frameFailure.Code
        : null;
}

public sealed class InteriorFrameException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
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
