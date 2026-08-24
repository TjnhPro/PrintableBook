using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class BorderLineDetectionResultTests
{
    [Fact]
    public void NoBorder_preserves_evaluated_sides_without_returning_bounds()
    {
        var result = BorderLineDetectionResult.NoBorder(
            left: BorderLineSideResult.Detected(12),
            right: BorderLineSideResult.Missing());

        Assert.False(result.HasBorder);
        Assert.Equal(12, result.Left.Position);
        Assert.False(result.Right.Found);
        Assert.False(result.Top.Found);
        Assert.False(result.Bottom.Found);
        Assert.Null(result.BorderBounds);
    }

    [Fact]
    public void Detected_retains_all_side_coordinates_and_bounds()
    {
        var bounds = new ImageRectangle(new ImagePoint(10, 20), new ImageSize(100, 200));
        var result = BorderLineDetectionResult.Detected(
            BorderLineSideResult.Detected(10),
            BorderLineSideResult.Detected(109),
            BorderLineSideResult.Detected(20),
            BorderLineSideResult.Detected(219),
            bounds);

        Assert.True(result.HasBorder);
        Assert.Equal(bounds, result.BorderBounds);
        Assert.Equal(10, result.Left.Position);
        Assert.Equal(109, result.Right.Position);
        Assert.Equal(20, result.Top.Position);
        Assert.Equal(219, result.Bottom.Position);
    }
}
