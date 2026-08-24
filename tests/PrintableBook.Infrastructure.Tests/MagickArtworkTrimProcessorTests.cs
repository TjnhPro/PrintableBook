using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickArtworkTrimProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.TrimTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task TrimAsync_crops_white_borders_while_retaining_near_black_artwork()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "source.png");
        var target = Path.Combine(rootPath, "trimmed.png");
        using (var image = new MagickImage(MagickColors.White, 100, 80))
        {
            image.GetPixels().SetPixel(20, 10, [15, 15, 15]);
            image.GetPixels().SetPixel(69, 59, [0, 0, 0]);
            image.Write(source);
        }

        var result = await new MagickArtworkTrimProcessor().TrimAsync(new ArtworkTrimRequest(
            new FileReference(source),
            new FileReference(target),
            new ArtworkDetectionThreshold(20)));

        Assert.True(result.HasArtwork);
        Assert.Equal(new ImageRectangle(new ImagePoint(20, 10), new ImageSize(50, 50)), result.ArtworkBounds);
        var output = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize(50, 50), output.Size);
    }

    [Fact]
    public async Task TrimAsync_returns_no_artwork_without_creating_a_trimmed_output()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "white.png");
        var target = Path.Combine(rootPath, "trimmed.png");
        using (var image = new MagickImage(MagickColors.White, 32, 32))
        {
            image.Write(source);
        }

        var result = await new MagickArtworkTrimProcessor().TrimAsync(new ArtworkTrimRequest(
            new FileReference(source),
            new FileReference(target),
            new ArtworkDetectionThreshold(20)));

        Assert.False(result.HasArtwork);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task TrimAsync_ignores_fully_transparent_pixels_when_finding_artwork_bounds()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "transparent.png");
        var target = Path.Combine(rootPath, "trimmed.png");
        using (var image = new MagickImage(MagickColors.White, 20, 20))
        {
            image.Alpha(AlphaOption.On);
            for (var y = 0; y < 20; y++)
            {
                for (var x = 0; x < 20; x++)
                {
                    image.GetPixels().SetPixel(x, y, [255, 255, 255, 0]);
                }
            }
            image.GetPixels().SetPixel(5, 5, [0, 0, 0, 255]);
            image.GetPixels().SetPixel(14, 14, [0, 0, 0, 255]);
            image.Write(source);
        }

        var result = await new MagickArtworkTrimProcessor().TrimAsync(new ArtworkTrimRequest(
            new FileReference(source),
            new FileReference(target),
            new ArtworkDetectionThreshold(20)));

        Assert.True(result.HasArtwork);
        Assert.Equal(new ImageRectangle(new ImagePoint(5, 5), new ImageSize(10, 10)), result.ArtworkBounds);
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
}
