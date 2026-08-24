using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickArtworkResizeProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ResizeTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ResizeAsync_upscales_a_square_png_to_the_exact_target_without_changing_its_aspect_ratio()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "square.png");
        var target = Path.Combine(rootPath, "resized.png");
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            for (var y = 40; y < 60; y++)
            {
                for (var x = 20; x < 30; x++)
                {
                    image.GetPixels().SetPixel(x, y, [0, 0, 0]);
                }
            }
            image.Write(source);
        }

        await new MagickArtworkResizeProcessor().ResizeAsync(new ArtworkResizeRequest(
            new FileReference(source),
            new FileReference(target),
            400,
            new ImageDensity(300, 300)));

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize(400, 400), info.Size);
        Assert.NotNull(info.Density);
        Assert.Equal(300, info.Density.Value.Horizontal, precision: 2);
        using var output = new MagickImage(target);
        Assert.Equal((byte)0, output.GetPixels().GetPixel(100, 200)[0]);
    }

    [Fact]
    public async Task ResizeAsync_rejects_a_non_positive_maximum_side()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "square.png");
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            image.Write(source);
        }

        await Assert.ThrowsAnyAsync<ArgumentException>(() => new MagickArtworkResizeProcessor().ResizeAsync(new ArtworkResizeRequest(
            new FileReference(source),
            new FileReference(Path.Combine(rootPath, "target.png")),
            0,
            new ImageDensity(300, 300))).AsTask());
    }

    [Fact]
    public async Task ResizeAsync_flattens_transparency_onto_an_opaque_white_background()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "transparent.png");
        var target = Path.Combine(rootPath, "opaque.png");
        using (var image = new MagickImage(MagickColors.Transparent, 10, 10))
        {
            image.GetPixels().SetPixel(5, 5, [0, 0, 0, 255]);
            image.Write(source);
        }

        await new MagickArtworkResizeProcessor().ResizeAsync(new ArtworkResizeRequest(
            new FileReference(source),
            new FileReference(target),
            20,
            new ImageDensity(300, 300)));

        using var output = new MagickImage(target);
        var rgba = output.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException("Expected RGBA pixel data.");
        Assert.Equal([255, 255, 255, 255], rgba.Take(4));
        Assert.All(rgba.Where((_, index) => index % 4 == 3), alpha => Assert.Equal((byte)255, alpha));
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
