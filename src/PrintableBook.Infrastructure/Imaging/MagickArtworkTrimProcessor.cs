using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickArtworkTrimProcessor : IArtworkTrimProcessor
{
    public ValueTask<ArtworkTrimResult> TrimAsync(ArtworkTrimRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(request.Source.Value);
        var bounds = FindArtworkBounds(image, request.Threshold, cancellationToken);
        if (bounds is null)
        {
            return ValueTask.FromResult(ArtworkTrimResult.NoArtwork());
        }

        image.Crop(new MagickGeometry(
            bounds.Value.Origin.X,
            bounds.Value.Origin.Y,
            (uint)bounds.Value.Size.Width,
            (uint)bounds.Value.Size.Height));
        image.ResetPage();
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The trim target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        image.Write(request.Target.Value);

        return ValueTask.FromResult(ArtworkTrimResult.Trimmed(bounds.Value));
    }

    private static ImageRectangle? FindArtworkBounds(
        MagickImage image,
        ArtworkDetectionThreshold threshold,
        CancellationToken cancellationToken)
    {
        var minimumX = (int)image.Width;
        var minimumY = (int)image.Height;
        var maximumX = -1;
        var maximumY = -1;
        var pixels = image.GetPixels();
        var rgba = pixels.ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidDataException("Unable to read source raster pixels.");

        for (var y = 0; y < (int)image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < (int)image.Width; x++)
            {
                var offset = checked(((y * (int)image.Width) + x) * 4);
                if (!IsNearBlack(rgba, offset, threshold.Value))
                {
                    continue;
                }

                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }
        }

        return maximumX < 0
            ? null
            : new ImageRectangle(
                new ImagePoint(minimumX, minimumY),
                new ImageSize(maximumX - minimumX + 1, maximumY - minimumY + 1));
    }

    private static bool IsNearBlack(byte[] rgba, int offset, byte threshold) =>
        rgba[offset + 3] >= 128 &&
        rgba[offset] <= threshold &&
        rgba[offset + 1] <= threshold &&
        rgba[offset + 2] <= threshold;
}
