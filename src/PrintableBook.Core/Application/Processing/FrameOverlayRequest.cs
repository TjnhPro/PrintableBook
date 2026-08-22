using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record FrameOverlayRequest(
    FileReference Source,
    FileReference Target,
    FileReference? Frame,
    bool Enabled);
