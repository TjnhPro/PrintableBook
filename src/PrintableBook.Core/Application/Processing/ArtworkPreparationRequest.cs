using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Input for preparing classified source artwork into a square product raster.
/// </summary>
public sealed record ArtworkPreparationRequest(
    FileReference Source,
    FileReference Target,
    EffectiveArtworkClassification Classification,
    ArtworkDetectionThreshold ArtworkDetectionThreshold,
    ImageSize PreparedArtworkSize,
    ImageDensity TargetDensity)
{
    public ArtworkPreparationRequest(
        FileReference source,
        FileReference target,
        ArtworkClassificationResult classification,
        ArtworkDetectionThreshold artworkDetectionThreshold,
        ImageSize preparedArtworkSize,
        ImageDensity targetDensity)
        : this(
            source,
            target,
            EffectiveArtworkClassification.FromDetection(classification),
            artworkDetectionThreshold,
            preparedArtworkSize,
            targetDensity)
    {
    }
}
