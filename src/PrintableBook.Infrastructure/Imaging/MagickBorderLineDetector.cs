using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Detects four continuous near-black outer lines using narrow, center-spanning RGBA regions.
/// </summary>
public sealed class MagickBorderLineDetector : IBorderLineDetector
{
    private const int SearchDepth = 100;
    private const int SearchBandSize = SearchDepth + 1;
    private const int SampleHalfSpan = 300;
    private const byte MinimumOpaqueAlpha = 128;

    public ValueTask<BorderLineDetectionResult> DetectAsync(
        BorderLineDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(request.Source.Value);
        using var pixels = image.GetPixels();
        var imageWidth = checked((int)image.Width);
        var imageHeight = checked((int)image.Height);
        var verticalSample = CalculateCenterSpan(imageHeight);
        var horizontalSample = CalculateCenterSpan(imageWidth);
        var verticalBandWidth = Math.Min(imageWidth, SearchBandSize);
        var horizontalBandHeight = Math.Min(imageHeight, SearchBandSize);

        var left = FindVerticalSide(
            pixels, 0, verticalSample.Start, verticalBandWidth, verticalSample.Length,
            searchFromHighCoordinate: false, request.Threshold.Value, cancellationToken);
        if (!left.Found)
        {
            return ValueTask.FromResult(BorderLineDetectionResult.NoBorder(left: left));
        }

        var right = FindVerticalSide(
            pixels, imageWidth - verticalBandWidth, verticalSample.Start, verticalBandWidth, verticalSample.Length,
            searchFromHighCoordinate: true, request.Threshold.Value, cancellationToken);
        if (!right.Found)
        {
            return ValueTask.FromResult(BorderLineDetectionResult.NoBorder(left, right));
        }

        var top = FindHorizontalSide(
            pixels, horizontalSample.Start, 0, horizontalSample.Length, horizontalBandHeight,
            searchFromHighCoordinate: false, request.Threshold.Value, cancellationToken);
        if (!top.Found)
        {
            return ValueTask.FromResult(BorderLineDetectionResult.NoBorder(left, right, top));
        }

        var bottom = FindHorizontalSide(
            pixels, horizontalSample.Start, imageHeight - horizontalBandHeight, horizontalSample.Length, horizontalBandHeight,
            searchFromHighCoordinate: true, request.Threshold.Value, cancellationToken);
        if (!bottom.Found)
        {
            return ValueTask.FromResult(BorderLineDetectionResult.NoBorder(left, right, top, bottom));
        }

        if (right.Position < left.Position || bottom.Position < top.Position)
        {
            throw new InvalidDataException("Detected border geometry is invalid.");
        }

        var leftPosition = left.Position ?? throw new InvalidDataException("A detected left border has no position.");
        var rightPosition = right.Position ?? throw new InvalidDataException("A detected right border has no position.");
        var topPosition = top.Position ?? throw new InvalidDataException("A detected top border has no position.");
        var bottomPosition = bottom.Position ?? throw new InvalidDataException("A detected bottom border has no position.");
        var bounds = new ImageRectangle(
            new ImagePoint(leftPosition, topPosition),
            new ImageSize(
                rightPosition - leftPosition + 1,
                bottomPosition - topPosition + 1));
        return ValueTask.FromResult(BorderLineDetectionResult.Detected(left, right, top, bottom, bounds));
    }

    private static BorderLineSideResult FindVerticalSide(
        IPixelCollection<byte> pixels,
        int roiX,
        int roiY,
        int roiWidth,
        int roiHeight,
        bool searchFromHighCoordinate,
        byte threshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rgba = pixels.ToByteArray(roiX, roiY, (uint)roiWidth, (uint)roiHeight, PixelMapping.RGBA)
            ?? throw new InvalidDataException("Unable to read the left or right border region.");
        var candidates = Enumerable.Repeat(true, roiWidth).ToArray();
        var remaining = roiWidth;

        for (var row = 0; row < roiHeight; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowOffset = row * roiWidth * 4;
            for (var column = 0; column < roiWidth; column++)
            {
                if (!candidates[column])
                {
                    continue;
                }

                var pixelOffset = rowOffset + (column * 4);
                if (IsBlack(rgba, pixelOffset, threshold))
                {
                    continue;
                }

                candidates[column] = false;
                remaining--;
            }

            if (remaining == 0)
            {
                return BorderLineSideResult.Missing();
            }
        }

        var start = searchFromHighCoordinate ? roiWidth - 1 : 0;
        var end = searchFromHighCoordinate ? -1 : roiWidth;
        var step = searchFromHighCoordinate ? -1 : 1;
        for (var column = start; column != end; column += step)
        {
            if (candidates[column])
            {
                return BorderLineSideResult.Detected(roiX + column);
            }
        }

        return BorderLineSideResult.Missing();
    }

    private static BorderLineSideResult FindHorizontalSide(
        IPixelCollection<byte> pixels,
        int roiX,
        int roiY,
        int roiWidth,
        int roiHeight,
        bool searchFromHighCoordinate,
        byte threshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rgba = pixels.ToByteArray(roiX, roiY, (uint)roiWidth, (uint)roiHeight, PixelMapping.RGBA)
            ?? throw new InvalidDataException("Unable to read the top or bottom border region.");
        var start = searchFromHighCoordinate ? roiHeight - 1 : 0;
        var end = searchFromHighCoordinate ? -1 : roiHeight;
        var step = searchFromHighCoordinate ? -1 : 1;

        for (var row = start; row != end; row += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowOffset = row * roiWidth * 4;
            var isContinuousLine = true;
            for (var column = 0; column < roiWidth; column++)
            {
                if (IsBlack(rgba, rowOffset + (column * 4), threshold))
                {
                    continue;
                }

                isContinuousLine = false;
                break;
            }

            if (isContinuousLine)
            {
                return BorderLineSideResult.Detected(roiY + row);
            }
        }

        return BorderLineSideResult.Missing();
    }

    private static bool IsBlack(byte[] rgba, int offset, byte threshold) =>
        rgba[offset] <= threshold &&
        rgba[offset + 1] <= threshold &&
        rgba[offset + 2] <= threshold &&
        rgba[offset + 3] >= MinimumOpaqueAlpha;

    private static (int Start, int Length) CalculateCenterSpan(int length)
    {
        var center = length / 2;
        var start = Math.Max(0, center - SampleHalfSpan);
        var end = Math.Min(length - 1, center + SampleHalfSpan);
        return (start, end - start + 1);
    }
}
