using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class BorderPixelDetectionResultTests
{
    [Fact]
    public void None_returns_no_perimeter_contact()
    {
        var result = BorderPixelDetectionResult.None();

        Assert.False(result.HasBorderPixel);
        Assert.False(result.LeftHit);
        Assert.False(result.RightHit);
        Assert.False(result.TopHit);
        Assert.False(result.BottomHit);
    }

    [Fact]
    public void Detected_returns_positive_when_left_side_is_hit()
    {
        var result = BorderPixelDetectionResult.Detected(leftHit: true, rightHit: false, topHit: false, bottomHit: false);

        Assert.True(result.HasBorderPixel);
        Assert.True(result.LeftHit);
        Assert.False(result.RightHit);
        Assert.False(result.TopHit);
        Assert.False(result.BottomHit);
    }

    [Fact]
    public void Detected_returns_positive_when_only_bottom_side_is_hit()
    {
        var result = BorderPixelDetectionResult.Detected(leftHit: false, rightHit: false, topHit: false, bottomHit: true);

        Assert.True(result.HasBorderPixel);
        Assert.False(result.LeftHit);
        Assert.False(result.RightHit);
        Assert.False(result.TopHit);
        Assert.True(result.BottomHit);
    }

    [Fact]
    public void Detected_returns_negative_when_no_side_is_hit()
    {
        var result = BorderPixelDetectionResult.Detected(leftHit: false, rightHit: false, topHit: false, bottomHit: false);

        Assert.False(result.HasBorderPixel);
    }
}
