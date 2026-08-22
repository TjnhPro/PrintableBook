using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record ArtworkTrimRequest(
    FileReference Source,
    FileReference Target,
    ArtworkDetectionThreshold Threshold);
