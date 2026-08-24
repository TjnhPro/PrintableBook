using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Input for preparing classified source artwork into a square product raster.
/// </summary>
public sealed record ArtworkPreparationRequest(
    FileReference Source,
    FileReference Target,
    ArtworkClassificationResult Classification,
    ArtworkDetectionThreshold ArtworkDetectionThreshold,
    ImageSize PreparedArtworkSize,
    ImageDensity TargetDensity);
