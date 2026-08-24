using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;

namespace PrintableBook.Infrastructure.Tests;

public sealed class FullArtPreparationProcessorTests : IAsyncLifetime
{
    private static readonly ImageSize PreparedSize = new(2270, 2270);
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.FullArtPreparationTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(80, 40, true)]
    [InlineData(40, 80, false)]
    [InlineData(40, 40, true)]
    public async Task PrepareAsync_trims_then_center_crops_to_a_square_before_resizing(int width, int height, bool landscape)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{width}x{height}.png");
        var target = Path.Combine(rootPath, $"{width}x{height}.prepared.png");
        using (var image = CreateSource(width, height, landscape))
        {
            image.Write(source);
        }

        await CreateProcessor().PrepareAsync(CreateRequest(source, target));

        using var output = new MagickImage(target);
        Assert.Equal((uint)2270, output.Width);
        Assert.Equal((uint)2270, output.Height);
        AssertRgb(output, 1134, 1134, 0, 200, 0);
        if (landscape && width != height)
        {
            AssertRgb(output, 284, 1134, 255, 255, 255);
        }
        else if (!landscape)
        {
            AssertRgb(output, 1134, 284, 255, 255, 255);
        }
    }

    [Fact]
    public async Task PrepareAsync_fails_when_trim_finds_no_artwork()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "white.png");
        using (var image = new MagickImage(MagickColors.White, 20, 20))
        {
            image.Write(source);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateProcessor().PrepareAsync(
            CreateRequest(source, Path.Combine(rootPath, "white.prepared.png"))).AsTask());
    }

    [Fact]
    public async Task PrepareAsync_removes_white_margin_before_center_cropping()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "white-margin.png");
        var target = Path.Combine(rootPath, "white-margin.prepared.png");
        using (var image = new MagickImage(MagickColors.White, 100, 80))
        {
            var pixels = image.GetPixels();
            pixels.SetPixel(20, 20, [0, 0, 0]);
            pixels.SetPixel(79, 59, [0, 0, 0]);
            pixels.SetPixel(21, 39, [200, 0, 0]);
            Fill(pixels, 35, 30, 5, 5, [0, 0, 200]);
            Fill(pixels, 45, 35, 10, 10, [0, 200, 0]);
            image.Write(source);
        }

        await CreateProcessor().PrepareAsync(CreateRequest(source, target));

        using var output = new MagickImage(target);
        AssertRgb(output, 1134, 1134, 0, 200, 0);
        AssertRgb(output, 426, 710, 0, 0, 200);
        AssertRgb(output, 284, 1134, 255, 255, 255);
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

    private static FullArtPreparationProcessor CreateProcessor() => new(
        new MagickArtworkTrimProcessor(),
        new MagickSquareCropProcessor(),
        new MagickArtworkResizeProcessor());

    private static ArtworkPreparationRequest CreateRequest(string source, string target) =>
        new(
            new FileReference(source),
            new FileReference(target),
            new ArtworkClassificationResult(
                ArtworkType.FullArt,
                BorderLineDetectionResult.NoBorder(),
                BorderPixelDetectionResult.Detected(true, false, false, false)),
            new ArtworkDetectionThreshold(20),
            PreparedSize,
            new ImageDensity(300, 300));

    private static MagickImage CreateSource(int width, int height, bool landscape)
    {
        var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        var pixels = image.GetPixels();
        pixels.SetPixel(0, 0, [0, 0, 0]);
        pixels.SetPixel(width - 1, height - 1, [0, 0, 0]);

        if (landscape)
        {
            Fill(pixels, 5, (height / 2) - 5, 10, 10, [200, 0, 0]);
            Fill(pixels, (width / 2) - 5, (height / 2) - 5, 10, 10, [0, 200, 0]);
        }
        else
        {
            Fill(pixels, (width / 2) - 5, 5, 10, 10, [200, 0, 0]);
            Fill(pixels, (width / 2) - 5, (height / 2) - 5, 10, 10, [0, 200, 0]);
        }

        return image;
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
