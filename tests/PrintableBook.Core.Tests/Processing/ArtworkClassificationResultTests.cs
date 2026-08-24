using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class ArtworkClassificationResultTests
{
    [Fact]
    public void BorderArt_retains_border_line_evidence_without_border_pixel_evidence()
    {
        var borderLine = CreateBorderLine();

        var result = new ArtworkClassificationResult(ArtworkType.BorderArt, borderLine, null);

        Assert.Equal(ArtworkType.BorderArt, result.Type);
        Assert.Same(borderLine, result.BorderLine);
        Assert.Null(result.BorderPixel);
    }

    [Theory]
    [InlineData(ArtworkType.FullArt)]
    [InlineData(ArtworkType.CropArt)]
    public void Non_border_art_retains_no_border_line_and_border_pixel_evidence(ArtworkType type)
    {
        var borderLine = BorderLineDetectionResult.NoBorder();
        var borderPixel = BorderPixelDetectionResult.Detected(false, true, false, false);

        var result = new ArtworkClassificationResult(type, borderLine, borderPixel);

        Assert.Equal(type, result.Type);
        Assert.Same(borderLine, result.BorderLine);
        Assert.Same(borderPixel, result.BorderPixel);
    }

    [Fact]
    public void BorderArt_requires_positive_border_line_evidence()
    {
        Assert.Throws<ArgumentException>(() =>
            new ArtworkClassificationResult(ArtworkType.BorderArt, BorderLineDetectionResult.NoBorder(), null));
    }

    [Fact]
    public void Non_border_art_requires_border_pixel_evidence_after_a_negative_border_line()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ArtworkClassificationResult(ArtworkType.FullArt, BorderLineDetectionResult.NoBorder(), null));
    }

    [Fact]
    public void BorderArt_rejects_border_pixel_evidence()
    {
        Assert.Throws<ArgumentException>(() =>
            new ArtworkClassificationResult(
                ArtworkType.BorderArt,
                CreateBorderLine(),
                BorderPixelDetectionResult.Detected(true, false, false, false)));
    }

    private static BorderLineDetectionResult CreateBorderLine() =>
        BorderLineDetectionResult.Detected(
            BorderLineSideResult.Detected(10),
            BorderLineSideResult.Detected(90),
            BorderLineSideResult.Detected(10),
            BorderLineSideResult.Detected(90),
            new ImageRectangle(new ImagePoint(10, 10), new ImageSize(81, 81)));
}
