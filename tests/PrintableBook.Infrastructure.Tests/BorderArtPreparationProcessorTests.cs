using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;

namespace PrintableBook.Infrastructure.Tests;

public sealed class BorderArtPreparationProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BorderArtPreparationTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(80, 60)]
    [InlineData(60, 80)]
    [InlineData(60, 60)]
    [InlineData(81, 60)]
    public async Task PrepareAsync_removes_the_detected_border_and_retains_centered_interior_content(int borderWidth, int borderHeight)
    {
        Directory.CreateDirectory(rootPath);
        const int origin = 10;
        var source = Path.Combine(rootPath, $"{borderWidth}x{borderHeight}.png");
        var target = Path.Combine(rootPath, $"{borderWidth}x{borderHeight}.prepared.png");
        using (var image = CreateSource(borderWidth, borderHeight, origin))
        {
            image.Write(source);
        }

        await CreateProcessor().PrepareAsync(CreateRequest(source, target, borderWidth, borderHeight, origin));

        using var output = new MagickImage(target);
        Assert.Equal((uint)2270, output.Width);
        Assert.Equal((uint)2270, output.Height);
        AssertRgb(output, 1134, 1134, 0, 200, 0);
        AssertRgb(output, 0, 0, 255, 255, 255);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static BorderArtPreparationProcessor CreateProcessor() => new(
        new MagickBorderBoundsCropProcessor(),
        new MagickSquareCropProcessor(),
        new MagickArtworkResizeProcessor());

    private static ArtworkPreparationRequest CreateRequest(string source, string target, int borderWidth, int borderHeight, int origin)
    {
        var bounds = new ImageRectangle(new ImagePoint(origin, origin), new ImageSize(borderWidth, borderHeight));
        return new ArtworkPreparationRequest(
            new FileReference(source),
            new FileReference(target),
            new ArtworkClassificationResult(
                ArtworkType.BorderArt,
                BorderLineDetectionResult.Detected(
                    BorderLineSideResult.Detected(origin),
                    BorderLineSideResult.Detected(origin + borderWidth - 1),
                    BorderLineSideResult.Detected(origin),
                    BorderLineSideResult.Detected(origin + borderHeight - 1),
                    bounds),
                null),
            new ArtworkDetectionThreshold(20),
            new ImageSize(2270, 2270),
            new ImageDensity(300, 300));
    }

    private static MagickImage CreateSource(int borderWidth, int borderHeight, int origin)
    {
        var image = new MagickImage(MagickColors.White, (uint)(borderWidth + (origin * 2)), (uint)(borderHeight + (origin * 2)));
        var pixels = image.GetPixels();
        PaintRectangleOutline(pixels, origin, origin, origin + borderWidth - 1, origin + borderHeight - 1, [0, 0, 0]);
        var centerX = origin + (borderWidth / 2);
        var centerY = origin + (borderHeight / 2);
        Fill(pixels, centerX - 10, centerY - 10, 20, 20, [0, 200, 0]);
        return image;
    }

    private static void PaintRectangleOutline(IPixelCollection<byte> pixels, int left, int top, int right, int bottom, byte[] color)
    {
        for (var x = left; x <= right; x++)
        {
            pixels.SetPixel(x, top, color);
            pixels.SetPixel(x, bottom, color);
        }

        for (var y = top; y <= bottom; y++)
        {
            pixels.SetPixel(left, y, color);
            pixels.SetPixel(right, y, color);
        }
    }

    private static void Fill(IPixelCollection<byte> pixels, int x, int y, int width, int height, byte[] color)
    {
        for (var currentY = y; currentY < y + height; currentY++)
        {
            for (var currentX = x; currentX < x + width; currentX++)
            {
                pixels.SetPixel(currentX, currentY, color);
            }
        }
    }

    private static void AssertRgb(MagickImage image, int x, int y, byte red, byte green, byte blue)
    {
        var pixel = image.GetPixels().GetPixel(x, y);
        Assert.Equal(red, pixel[0]);
        Assert.Equal(green, pixel[1]);
        Assert.Equal(blue, pixel[2]);
    }
}
