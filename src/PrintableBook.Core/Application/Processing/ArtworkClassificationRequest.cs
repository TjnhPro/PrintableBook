using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Input for classifying an unmodified source artwork raster.
/// </summary>
public sealed record ArtworkClassificationRequest(
    FileReference Source,
    ArtworkDetectionThreshold Threshold);
