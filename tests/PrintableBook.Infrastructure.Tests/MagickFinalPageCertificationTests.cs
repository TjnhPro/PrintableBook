using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickFinalPageCertificationTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.FinalPageCertification.{Guid.NewGuid():N}");

    [Fact]
    public async Task ProduceAsync_centers_a_2550_working_page_at_exact_floor_offsets_in_a_2588_by_2625_final_page()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "working.png");
        var target = Path.Combine(rootPath, "final.png");
        using (var working = new MagickImage(MagickColors.White, 2550, 2550))
        {
            var pixels = working.GetPixels();
            pixels.SetPixel(0, 0, [0, 0, 0]);
            pixels.SetPixel(2549, 2549, [0, 0, 0]);
            pixels.SetPixel(1275, 1275, [0, 0, 0]);
            working.Write(source);
        }

        await new MagickFinalInteriorPageProcessor().ProduceAsync(new FinalInteriorPageRequest(
            new FileReference(source), new FileReference(target), new ImageSize(2588, 2625), new ImageDensity(300, 300)));

        using var finalPage = new MagickImage(target);
        var outputPixels = finalPage.GetPixels();
        Assert.Equal((uint)2588, finalPage.Width);
        Assert.Equal((uint)2625, finalPage.Height);
        Assert.Equal((byte)255, outputPixels.GetPixel(18, 37)[0]);
        Assert.Equal((byte)0, outputPixels.GetPixel(19, 37)[0]);
        Assert.Equal((byte)0, outputPixels.GetPixel(2568, 2586)[0]);
        Assert.Equal((byte)255, outputPixels.GetPixel(2569, 2586)[0]);
        Assert.Equal((byte)255, outputPixels.GetPixel(19, 36)[0]);
        Assert.Equal((byte)255, outputPixels.GetPixel(19, 2587)[0]);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
