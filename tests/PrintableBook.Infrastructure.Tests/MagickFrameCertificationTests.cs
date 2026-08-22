using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickFrameCertificationTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.FrameCertification.{Guid.NewGuid():N}");

    [Theory]
    [InlineData("square", 500, 500)]
    [InlineData("portrait", 400, 600)]
    [InlineData("landscape", 600, 400)]
    public async Task ApplyAsync_overlays_a_same_size_transparent_frame_without_moving_artwork(string id, uint width, uint height)
    {
        Directory.CreateDirectory(rootPath);
        var page = Path.Combine(rootPath, $"{id}.page.png");
        var frame = Path.Combine(rootPath, $"{id}.frame.png");
        var target = Path.Combine(rootPath, $"{id}.output.png");
        WritePage(page, width, height);
        WriteFrame(frame, width, height);

        await new MagickFrameProcessor().ApplyAsync(new FrameOverlayRequest(
            new FileReference(page), new FileReference(target), new FileReference(frame), true));

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize((int)width, (int)height), info.Size);
        using var output = new MagickImage(target);
        Assert.Equal((byte)0, output.GetPixels().GetPixel((int)width / 2, (int)height / 2)[0]);
        Assert.Equal((byte)0, output.GetPixels().GetPixel(0, 0)[0]);
        Assert.Equal((byte)0, output.GetPixels().GetPixel((int)width - 1, (int)height - 1)[0]);
        CertificationArtifactStore.Capture("frame", id, page, frame, target);
    }

    [Fact]
    public async Task ApplyAsync_with_frame_disabled_copies_the_original_bytes_without_requiring_a_frame()
    {
        Directory.CreateDirectory(rootPath);
        var page = Path.Combine(rootPath, "off.page.png");
        var target = Path.Combine(rootPath, "off.output.png");
        WritePage(page, 400, 600);

        await new MagickFrameProcessor().ApplyAsync(new FrameOverlayRequest(
            new FileReference(page), new FileReference(target), null, false));

        Assert.Equal(await File.ReadAllBytesAsync(page), await File.ReadAllBytesAsync(target));
        CertificationArtifactStore.Capture("frame", "off", page, target);
    }

    [Fact]
    public async Task ApplyAsync_rejects_missing_wrong_size_and_corrupt_enabled_frames()
    {
        Directory.CreateDirectory(rootPath);
        var page = Path.Combine(rootPath, "error.page.png");
        WritePage(page, 400, 600);
        var processor = new MagickFrameProcessor();
        var output = new FileReference(Path.Combine(rootPath, "error.output.png"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => processor.ApplyAsync(new FrameOverlayRequest(
            new FileReference(page), output, new FileReference(Path.Combine(rootPath, "missing.png")), true)).AsTask());

        var wrong = Path.Combine(rootPath, "wrong.frame.png");
        WriteFrame(wrong, 400, 400);
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ApplyAsync(new FrameOverlayRequest(
            new FileReference(page), output, new FileReference(wrong), true)).AsTask());

        var corrupt = Path.Combine(rootPath, "corrupt.frame.png");
        await File.WriteAllTextAsync(corrupt, "not a PNG");
        await Assert.ThrowsAnyAsync<MagickException>(() => processor.ApplyAsync(new FrameOverlayRequest(
            new FileReference(page), output, new FileReference(corrupt), true)).AsTask());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private static void WritePage(string path, uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.White, width, height);
        image.GetPixels().SetPixel((int)width / 2, (int)height / 2, [0, 0, 0]);
        image.Write(path);
    }

    private static void WriteFrame(string path, uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.Transparent, width, height);
        var pixels = image.GetPixels();
        for (var x = 0; x < (int)width; x++)
        {
            pixels.SetPixel(x, 0, [0, 0, 0, 255]);
            pixels.SetPixel(x, (int)height - 1, [0, 0, 0, 255]);
        }
        for (var y = 0; y < (int)height; y++)
        {
            pixels.SetPixel(0, y, [0, 0, 0, 255]);
            pixels.SetPixel((int)width - 1, y, [0, 0, 0, 255]);
        }
        image.Write(path);
    }
}
