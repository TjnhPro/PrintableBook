namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Exact-perimeter ink evidence for each source raster side.
/// </summary>
public sealed record BorderPixelDetectionResult
{
    private BorderPixelDetectionResult(bool leftHit, bool rightHit, bool topHit, bool bottomHit)
    {
        LeftHit = leftHit;
        RightHit = rightHit;
        TopHit = topHit;
        BottomHit = bottomHit;
    }

    public bool LeftHit { get; }

    public bool RightHit { get; }

    public bool TopHit { get; }

    public bool BottomHit { get; }

    public bool HasBorderPixel => LeftHit || RightHit || TopHit || BottomHit;

    public static BorderPixelDetectionResult None() =>
        new(false, false, false, false);

    public static BorderPixelDetectionResult Detected(
        bool leftHit,
        bool rightHit,
        bool topHit,
        bool bottomHit) =>
        new(leftHit, rightHit, topHit, bottomHit);
}
