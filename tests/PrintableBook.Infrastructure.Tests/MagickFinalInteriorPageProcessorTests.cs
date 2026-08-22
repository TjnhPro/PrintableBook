using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickFinalInteriorPageProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.FinalPageTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ProduceAsync_writes_a_reopenable_final_png_with_the_configured_dimensions_and_density()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "framed.png");
        var target = Path.Combine(rootPath, "final.png");
        using (var image = new MagickImage(MagickColors.White, 200, 200))
        {
            image.GetPixels().SetPixel(100, 100, [0, 0, 0]);
            image.Write(source);
        }

        await new MagickFinalInteriorPageProcessor().ProduceAsync(new FinalInteriorPageRequest(
            new FileReference(source),
            new FileReference(target),
            new ImageSize(200, 200),
            new ImageDensity(300, 300)));

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize(200, 200), info.Size);
        Assert.NotNull(info.Density);
        Assert.Equal(300, info.Density.Value.Horizontal, precision: 2);
        using var finalImage = new MagickImage(target);
        Assert.Equal((byte)0, finalImage.GetPixels().GetPixel(100, 100)[0]);
    }

    [Fact]
    public async Task ProduceAsync_rejects_an_unexpected_cached_raster_size()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "wrong-size.png");
        using (var image = new MagickImage(MagickColors.White, 400, 400))
        {
            image.Write(source);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MagickFinalInteriorPageProcessor().ProduceAsync(new FinalInteriorPageRequest(
            new FileReference(source),
            new FileReference(Path.Combine(rootPath, "final.png")),
            new ImageSize(300, 300),
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
