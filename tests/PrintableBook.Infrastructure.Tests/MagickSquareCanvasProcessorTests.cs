using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickSquareCanvasProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.CanvasTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task NormalizeAsync_centers_a_portrait_image_on_an_exact_white_square_canvas()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "trimmed.png");
        var target = Path.Combine(rootPath, "square.png");
        using (var image = new MagickImage(MagickColors.White, 788, 900))
        {
            image.GetPixels().SetPixel(0, 0, [0, 0, 0]);
            image.Write(source);
        }

        await new MagickSquareCanvasProcessor().NormalizeAsync(new SquareCanvasRequest(
            new FileReference(source),
            new FileReference(target)));

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize(900, 900), info.Size);
        using var output = new MagickImage(target);
        Assert.Equal((byte)255, output.GetPixels().GetPixel(55, 0)[0]);
        Assert.Equal((byte)0, output.GetPixels().GetPixel(56, 0)[0]);
    }

    [Fact]
    public async Task NormalizeAsync_keeps_an_existing_square_without_resizing_it()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "square-source.png");
        var target = Path.Combine(rootPath, "square-target.png");
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            image.GetPixels().SetPixel(99, 99, [0, 0, 0]);
            image.Write(source);
        }

        await new MagickSquareCanvasProcessor().NormalizeAsync(new SquareCanvasRequest(
            new FileReference(source),
            new FileReference(target)));

        using var output = new MagickImage(target);
        Assert.Equal((uint)100, output.Width);
        Assert.Equal((byte)0, output.GetPixels().GetPixel(99, 99)[0]);
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
