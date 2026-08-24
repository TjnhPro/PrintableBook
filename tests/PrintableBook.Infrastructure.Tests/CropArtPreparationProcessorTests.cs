using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;

namespace PrintableBook.Infrastructure.Tests;

public sealed class CropArtPreparationProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.CropArtPreparationTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(60, 30)]
    [InlineData(30, 60)]
    [InlineData(40, 40)]
    public async Task PrepareAsync_trims_then_pads_without_discarding_trimmed_content(int artworkWidth, int artworkHeight)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{artworkWidth}x{artworkHeight}.png");
        var target = Path.Combine(rootPath, $"{artworkWidth}x{artworkHeight}.prepared.png");
        using (var image = CreateSource(artworkWidth, artworkHeight))
        {
            image.Write(source);
        }

        await CreateProcessor().PrepareAsync(CreateRequest(source, target));

        using var output = new MagickImage(target);
        Assert.Equal((uint)2270, output.Width);
        Assert.Equal((uint)2270, output.Height);
        var side = Math.Max(artworkWidth, artworkHeight);
        var offsetX = (side - artworkWidth) / 2;
        var offsetY = (side - artworkHeight) / 2;
        AssertRgb(output, Scale(14 + offsetX, side), Scale(14 + offsetY, side), 200, 0, 0);
        if (artworkWidth != artworkHeight)
        {
            AssertRgb(output, 0, 0, 255, 255, 255);
        }
        AssertOpaque(output);
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

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static CropArtPreparationProcessor CreateProcessor() => new(
        new MagickArtworkTrimProcessor(),
        new MagickSquarePadProcessor(),
        new MagickArtworkResizeProcessor());

    private static ArtworkPreparationRequest CreateRequest(string source, string target) =>
        new(
            new FileReference(source),
            new FileReference(target),
            new ArtworkClassificationResult(
                ArtworkType.CropArt,
                BorderLineDetectionResult.NoBorder(),
                BorderPixelDetectionResult.None()),
            new ArtworkDetectionThreshold(20),
            new ImageSize(2270, 2270),
            new ImageDensity(300, 300));

    private static MagickImage CreateSource(int artworkWidth, int artworkHeight)
    {
        const int margin = 20;
        var image = new MagickImage(MagickColors.White, (uint)(artworkWidth + (margin * 2)), (uint)(artworkHeight + (margin * 2)));
        var pixels = image.GetPixels();
        pixels.SetPixel(margin, margin, [0, 0, 0]);
        pixels.SetPixel(margin + artworkWidth - 1, margin + artworkHeight - 1, [0, 0, 0]);
        Fill(pixels, margin + 10, margin + 10, 10, 10, [200, 0, 0]);
        return image;
    }

    private static int Scale(int sourceCoordinate, int side) =>
        checked((int)Math.Round(sourceCoordinate * (2270d / side), MidpointRounding.AwayFromZero));

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

    private static void AssertOpaque(MagickImage image)
    {
        var rgba = image.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException("Expected RGBA pixel data.");
        Assert.All(rgba.Where((_, index) => index % 4 == 3), alpha => Assert.Equal((byte)255, alpha));
    }
}
