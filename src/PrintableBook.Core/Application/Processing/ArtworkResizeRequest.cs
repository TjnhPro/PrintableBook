using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record ArtworkResizeRequest(
    FileReference Source,
    FileReference Target,
    ImageSize TargetSize,
    ImageDensity TargetDensity);
