using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickFrameProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.FrameTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ApplyAsync_composites_a_matching_frame_over_the_page_without_changing_page_dimensions()
    {
        Directory.CreateDirectory(rootPath);
        var page = Path.Combine(rootPath, "page.png");
        var frame = Path.Combine(rootPath, "frame.png");
        var target = Path.Combine(rootPath, "framed.png");
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            image.GetPixels().SetPixel(50, 50, [0, 0, 0]);
            image.Write(page);
        }
        using (var image = new MagickImage(MagickColors.Transparent, 100, 100))
        {
            image.GetPixels().SetPixel(0, 0, [255, 0, 0, 255]);
            image.Write(frame);
        }

        await new MagickFrameProcessor().ApplyAsync(new FrameOverlayRequest(
            new FileReference(page),
            new FileReference(target),
            new FileReference(frame),
            Enabled: true));

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize(100, 100), info.Size);
        using var output = new MagickImage(target);
        Assert.Equal((byte)255, output.GetPixels().GetPixel(0, 0)[0]);
        Assert.Equal((byte)0, output.GetPixels().GetPixel(50, 50)[0]);
    }

    [Fact]
    public async Task ApplyAsync_copies_the_page_when_the_frame_is_disabled()
    {
        Directory.CreateDirectory(rootPath);
        var page = Path.Combine(rootPath, "page.png");
        var target = Path.Combine(rootPath, "unframed.png");
        using (var image = new MagickImage(MagickColors.White, 40, 40))
        {
            image.GetPixels().SetPixel(5, 5, [0, 0, 0]);
            image.Write(page);
        }

        await new MagickFrameProcessor().ApplyAsync(new FrameOverlayRequest(
            new FileReference(page),
            new FileReference(target),
            Frame: null,
            Enabled: false));

        using var output = new MagickImage(target);
        Assert.Equal((byte)0, output.GetPixels().GetPixel(5, 5)[0]);
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
