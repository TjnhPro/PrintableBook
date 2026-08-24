namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Exact-perimeter ink evidence for each source raster side.
/// </summary>
public sealed record BorderPixelDetectionResult(
    bool HasBorderPixel,
    bool LeftHit,
    bool RightHit,
    bool TopHit,
    bool BottomHit)
{
    public static BorderPixelDetectionResult None() =>
        new(false, false, false, false, false);

    public static BorderPixelDetectionResult Detected(
        bool leftHit,
        bool rightHit,
        bool topHit,
        bool bottomHit) =>
        new(leftHit || rightHit || topHit || bottomHit, leftHit, rightHit, topHit, bottomHit);
}
