using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Input for read-only exact-perimeter ink detection on the original source raster.
/// </summary>
public sealed record BorderPixelDetectionRequest(
    FileReference Source,
    ArtworkDetectionThreshold Threshold);
