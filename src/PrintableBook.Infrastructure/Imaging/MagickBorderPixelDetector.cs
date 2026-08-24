using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Detects visible near-black ink on the exact one-pixel perimeter of the original artwork raster.
/// </summary>
public sealed class MagickBorderPixelDetector : IBorderPixelDetector
{
    private const byte MinimumVisibleAlpha = 128;

    public ValueTask<BorderPixelDetectionResult> DetectAsync(
        BorderPixelDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(request.Source.Value);
        using var pixels = image.GetPixels();
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);

        var leftHit = ReadAndScanSide(pixels, 0, 0, 1, height, "left", request.Threshold.Value, cancellationToken);
        var rightHit = ReadAndScanSide(pixels, width - 1, 0, 1, height, "right", request.Threshold.Value, cancellationToken);
        var topHit = ReadAndScanSide(pixels, 0, 0, width, 1, "top", request.Threshold.Value, cancellationToken);
        var bottomHit = ReadAndScanSide(pixels, 0, height - 1, width, 1, "bottom", request.Threshold.Value, cancellationToken);

        return ValueTask.FromResult(BorderPixelDetectionResult.Detected(leftHit, rightHit, topHit, bottomHit));
    }

    private static bool ReadAndScanSide(
        IPixelCollection<byte> pixels,
        int x,
        int y,
        int width,
        int height,
        string side,
        byte threshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return HasInk(ReadRgba(pixels, x, y, width, height, side), threshold, cancellationToken);
    }

    private static byte[] ReadRgba(
        IPixelCollection<byte> pixels,
        int x,
        int y,
        int width,
        int height,
        string side) =>
        pixels.ToByteArray(x, y, (uint)width, (uint)height, PixelMapping.RGBA)
        ?? throw new InvalidDataException($"Unable to read the {side} exact-perimeter raster region.");

    private static bool HasInk(byte[] rgba, byte threshold, CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            if (offset % 2048 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (IsInk(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3], threshold))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInk(byte red, byte green, byte blue, byte alpha, byte threshold) =>
        alpha >= MinimumVisibleAlpha &&
        red <= threshold &&
        green <= threshold &&
        blue <= threshold;
}
