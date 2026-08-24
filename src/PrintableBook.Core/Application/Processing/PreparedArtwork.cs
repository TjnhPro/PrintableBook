using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// A type-specifically prepared raster that is ready for the shared page pipeline.
/// </summary>
public sealed record PreparedArtwork(
    FileReference File,
    ArtworkType Type,
    bool AutoFrameRecommended)
{
    /// <summary>
    /// Reconstructs the shared-stage policy for a previously prepared, cached artwork file.
    /// </summary>
    public static PreparedArtwork FromCached(FileReference file, ArtworkType type) => new(
        file,
        type,
        type is ArtworkType.BorderArt or ArtworkType.FullArt);
}
