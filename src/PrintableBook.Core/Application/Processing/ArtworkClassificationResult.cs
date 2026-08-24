namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// The artwork type together with the detector evidence that produced it.
/// </summary>
public sealed record ArtworkClassificationResult
{
    public ArtworkClassificationResult(
        ArtworkType type,
        BorderLineDetectionResult borderLine,
        BorderPixelDetectionResult? borderPixel)
    {
        ArgumentNullException.ThrowIfNull(borderLine);

        if (type == ArtworkType.BorderArt)
        {
            if (!borderLine.HasBorder)
            {
                throw new ArgumentException("BorderArt requires positive border-line evidence.", nameof(borderLine));
            }

            if (borderPixel is not null)
            {
                throw new ArgumentException("BorderArt must not retain border-pixel evidence.", nameof(borderPixel));
            }
        }
        else
        {
            if (borderLine.HasBorder)
            {
                throw new ArgumentException("Only BorderArt may retain positive border-line evidence.", nameof(borderLine));
            }

            if (borderPixel is null)
            {
                throw new ArgumentNullException(nameof(borderPixel), "FullArt and CropArt require border-pixel evidence.");
            }
        }

        Type = type;
        BorderLine = borderLine;
        BorderPixel = borderPixel;
    }

    public ArtworkType Type { get; }

    public BorderLineDetectionResult BorderLine { get; }

    public BorderPixelDetectionResult? BorderPixel { get; }
}
