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
            new ImageSize(400, 400),
            new ImageDensity(300, 300)));

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize(400, 400), info.Size);
        Assert.NotNull(info.Density);
        Assert.Equal(300, info.Density.Value.Horizontal, precision: 2);
        using var output = new MagickImage(target);
        Assert.Equal((byte)0, output.GetPixels().GetPixel(100, 200)[0]);
    }

    [Fact]
    public async Task ResizeAsync_rejects_a_non_square_target_for_normalized_artwork()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "square.png");
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            image.Write(source);
        }

        await Assert.ThrowsAsync<ArgumentException>(() => new MagickArtworkResizeProcessor().ResizeAsync(new ArtworkResizeRequest(
            new FileReference(source),
            new FileReference(Path.Combine(rootPath, "target.png")),
            new ImageSize(400, 300),
            new ImageDensity(300, 300))).AsTask());
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
