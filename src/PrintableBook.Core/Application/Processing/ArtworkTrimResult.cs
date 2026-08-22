using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record ArtworkTrimResult(bool HasArtwork, ImageRectangle? ArtworkBounds)
{
    public static ArtworkTrimResult NoArtwork() => new(false, null);

    public static ArtworkTrimResult Trimmed(ImageRectangle bounds) => new(true, bounds);
}
