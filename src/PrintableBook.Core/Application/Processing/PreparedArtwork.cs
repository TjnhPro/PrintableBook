using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// A type-specifically prepared raster that is ready for the shared page pipeline.
/// </summary>
public sealed record PreparedArtwork(
    FileReference File,
    ArtworkType Type,
    bool FrameAllowed);
