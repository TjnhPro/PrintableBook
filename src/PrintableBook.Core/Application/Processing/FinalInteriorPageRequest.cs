using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record FinalInteriorPageRequest(
    FileReference Source,
    FileReference Target,
    ImageSize ExpectedSize,
    ImageDensity TargetDensity);
