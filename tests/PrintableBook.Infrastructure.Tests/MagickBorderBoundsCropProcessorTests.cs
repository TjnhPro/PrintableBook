using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickBorderBoundsCropProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BorderBoundsCropTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task CropAsync_keeps_only_pixels_strictly_inside_the_inclusive_border_bounds()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "source.png");
        var target = Path.Combine(rootPath, "inside.png");
        using (var image = new MagickImage(MagickColors.White, 10, 10))
        {
            PaintRectangleOutline(image, left: 1, top: 1, right: 8, bottom: 8, [0, 0, 0]);
            image.GetPixels().SetPixel(2, 2, [11, 22, 33]);
            image.GetPixels().SetPixel(7, 7, [44, 55, 66]);
            image.GetPixels().SetPixel(0, 0, [77, 88, 99]);
            image.Write(source);
        }

        await new MagickBorderBoundsCropProcessor().CropAsync(new BorderBoundsCropRequest(
            new FileReference(source),
            new FileReference(target),
            new ImageRectangle(new ImagePoint(1, 1), new ImageSize(8, 8))));

        using var output = new MagickImage(target);
        Assert.Equal((uint)6, output.Width);
        Assert.Equal((uint)6, output.Height);
        AssertRgb(output, 0, 0, 11, 22, 33);
        AssertRgb(output, 5, 5, 44, 55, 66);
        AssertRgb(output, 0, 5, 255, 255, 255);
    }

    [Fact]
    public async Task CropAsync_retains_an_odd_sized_strict_inside_region_without_extra_inset()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "odd-source.png");
        var target = Path.Combine(rootPath, "odd-inside.png");
        using (var image = new MagickImage(MagickColors.White, 10, 10))
        {
            PaintRectangleOutline(image, left: 1, top: 2, right: 7, bottom: 8, [0, 0, 0]);
            image.GetPixels().SetPixel(2, 3, [10, 20, 30]);
            image.GetPixels().SetPixel(6, 7, [40, 50, 60]);
            image.Write(source);
        }

        await new MagickBorderBoundsCropProcessor().CropAsync(new BorderBoundsCropRequest(
            new FileReference(source),
            new FileReference(target),
            new ImageRectangle(new ImagePoint(1, 2), new ImageSize(7, 7))));

        using var output = new MagickImage(target);
        Assert.Equal((uint)5, output.Width);
        Assert.Equal((uint)5, output.Height);
        AssertRgb(output, 0, 0, 10, 20, 30);
        AssertRgb(output, 4, 4, 40, 50, 60);
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

    private static void PaintRectangleOutline(MagickImage image, int left, int top, int right, int bottom, byte[] color)
    {
        for (var x = left; x <= right; x++)
        {
            image.GetPixels().SetPixel(x, top, color);
            image.GetPixels().SetPixel(x, bottom, color);
        }

        for (var y = top; y <= bottom; y++)
        {
            image.GetPixels().SetPixel(left, y, color);
            image.GetPixels().SetPixel(right, y, color);
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
