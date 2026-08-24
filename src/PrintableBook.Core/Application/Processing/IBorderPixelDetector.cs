namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Detects qualifying ink that contacts the exact perimeter of an untrimmed artwork raster.
/// </summary>
public interface IBorderPixelDetector
{
    ValueTask<BorderPixelDetectionResult> DetectAsync(
        BorderPixelDetectionRequest request,
        CancellationToken cancellationToken = default);
}
