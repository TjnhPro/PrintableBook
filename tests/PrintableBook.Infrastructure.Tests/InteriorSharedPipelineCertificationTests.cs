using System.Text.Json;
using System.Security.Cryptography;
using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class InteriorSharedPipelineCertificationTests : IAsyncLifetime
{
    private static readonly ImageSize PreparedSize = new(2270, 2270);
    private static readonly ImageSize WorkingSize = new(2550, 2550);
    private static readonly ImageSize FinalSize = new(2588, 2625);
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.SharedPipelineCertification.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(ArtworkType.BorderArt, FrameMode.Auto, true, true)]
    [InlineData(ArtworkType.BorderArt, FrameMode.Disabled, true, false)]
    [InlineData(ArtworkType.FullArt, FrameMode.Auto, true, true)]
    [InlineData(ArtworkType.CropArt, FrameMode.Auto, true, false)]
    [InlineData(ArtworkType.CropArt, FrameMode.Enabled, true, true)]
    [InlineData(ArtworkType.CropArt, FrameMode.Enabled, false, false)]
    public async Task ProcessAsync_certifies_the_real_classified_shared_workflow(ArtworkType expectedType, FrameMode frameMode, bool frameAvailable, bool frameApplied)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{expectedType}.source.png");
        var frame = Path.Combine(rootPath, "frame.png");
        WriteSource(source, expectedType);
        if (frameAvailable) WriteFrame(frame);
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId($"{expectedType}-book"), new DirectoryReference(Path.Combine(rootPath, expectedType.ToString())));
        var request = new InteriorPagePipelineRequest(
            workspace,
            new FileReference(source),
            "page-01",
            new ArtworkDetectionThreshold(20),
            PreparedSize,
            WorkingSize,
            FinalSize,
            new ImageDensity(300, 300),
            frameAvailable ? new FileReference(frame) : null,
            frameMode);

        var result = await CreatePipeline().ProcessAsync(request);

        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        using var classification = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache, "classification.json")));
        Assert.Equal(expectedType.ToString().ToLowerInvariant(), classification.RootElement.GetProperty("Type").GetString());
        await AssertSizeAsync(Path.Combine(cache, "prepared.png"), PreparedSize);
        await AssertSizeAsync(Path.Combine(cache, "framed.png"), PreparedSize);
        await AssertSizeAsync(Path.Combine(cache, "working-page.png"), WorkingSize);
        await AssertSizeAsync(result.FinalPage.Value, FinalSize);

        var preparedPath = Path.Combine(cache, "prepared.png");
        var framedPath = Path.Combine(cache, "framed.png");
        var preparedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(preparedPath)));
        var framedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(framedPath)));
        using var framed = new MagickImage(framedPath);
        using var prepared = new MagickImage(preparedPath);
        using var working = new MagickImage(Path.Combine(cache, "working-page.png"));
        using var final = new MagickImage(result.FinalPage.Value);
        if (frameApplied)
        {
            Assert.NotEqual(preparedHash, framedHash);
            Assert.Equal((byte)255, framed.GetPixels().GetPixel(0, 0)[0]);
            Assert.Equal(prepared.GetPixels().GetPixel(1135, 1135)[0], framed.GetPixels().GetPixel(1135, 1135)[0]);
        }
        else
        {
            Assert.Equal(preparedHash, framedHash);
        }

        if (expectedType == ArtworkType.BorderArt)
        {
            Assert.Equal((byte)255, prepared.GetPixels().GetPixel(0, 0)[0]);
        }

        if (expectedType == ArtworkType.FullArt)
        {
            AssertContainsBlue(prepared, 70, 70, 140, 140);
        }

        Assert.Equal((byte)255, working.GetPixels().GetPixel(139, 140)[0]);
        Assert.Equal(framed.GetPixels().GetPixel(0, 0)[0], working.GetPixels().GetPixel(140, 140)[0]);
        Assert.Equal((byte)255, final.GetPixels().GetPixel(158, 177)[0]);
        Assert.Equal(framed.GetPixels().GetPixel(0, 0)[0], final.GetPixels().GetPixel(159, 177)[0]);
    }

    private static DiskBackedInteriorPagePipeline CreatePipeline() => new(
        new ArtworkClassifier(new MagickBorderLineDetector(), new MagickBorderPixelDetector()),
        new ArtworkPreparationService(
            new BorderArtPreparationProcessor(
                new MagickBorderBoundsCropProcessor(), new MagickSquareCropProcessor(), new MagickArtworkResizeProcessor()),
            new FullArtPreparationProcessor(
                new MagickArtworkTrimProcessor(), new MagickSquareCropProcessor(), new MagickArtworkResizeProcessor()),
            new CropArtPreparationProcessor(
                new MagickArtworkTrimProcessor(), new MagickSquarePadProcessor(), new MagickArtworkResizeProcessor()),
            new MagickImageInspector()),
        new MagickFrameProcessor(),
        new MagickWorkingPageProcessor(),
        new MagickFinalInteriorPageProcessor(),
        new MagickImageInspector());

    private static async Task AssertSizeAsync(string path, ImageSize expected) =>
        Assert.Equal(expected, (await new MagickImageInspector().GetInfoAsync(new FileReference(path))).Size);

    private static void WriteSource(string path, ArtworkType type)
    {
        using var image = new MagickImage(MagickColors.White, 240, 220);
        var pixels = image.GetPixels();
        switch (type)
        {
            case ArtworkType.BorderArt:
                PaintOutline(pixels, 5, 5, 234, 214, [0, 0, 0]);
                Fill(pixels, 95, 80, 40, 60, [0, 0, 0]);
                break;
            case ArtworkType.FullArt:
                Fill(pixels, 0, 80, 1, 60, [0, 0, 0]);
                Fill(pixels, 80, 50, 100, 120, [0, 0, 0]);
                Fill(pixels, 35, 55, 5, 5, [0, 0, 255]);
                break;
            case ArtworkType.CropArt:
                Fill(pixels, 90, 40, 50, 140, [0, 0, 0]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        image.Write(path);
    }

    private static void WriteFrame(string path)
    {
        using var image = new MagickImage(MagickColors.Transparent, 2270, 2270);
        var pixels = image.GetPixels();
        for (var offset = 0; offset < 8; offset++)
        {
            for (var x = 0; x < 2270; x++)
            {
                pixels.SetPixel(x, offset, [255, 0, 0, 255]);
                pixels.SetPixel(x, 2269 - offset, [255, 0, 0, 255]);
            }

            for (var y = 0; y < 2270; y++)
            {
                pixels.SetPixel(offset, y, [255, 0, 0, 255]);
                pixels.SetPixel(2269 - offset, y, [255, 0, 0, 255]);
            }
        }

        image.Write(path);
    }

    private static void PaintOutline(IPixelCollection<byte> pixels, int left, int top, int right, int bottom, byte[] color)
    {
        for (var x = left; x <= right; x++)
        {
            pixels.SetPixel(x, top, color);
            pixels.SetPixel(x, bottom, color);
        }

        for (var y = top; y <= bottom; y++)
        {
            pixels.SetPixel(left, y, color);
            pixels.SetPixel(right, y, color);
        }
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

    private static void AssertContainsBlue(MagickImage image, int left, int top, int right, int bottom)
    {
        var pixels = image.GetPixels();
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var pixel = pixels.GetPixel(x, y);
                if (pixel[0] == 0 && pixel[1] == 0 && pixel[2] == byte.MaxValue)
                {
                    return;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("Expected the FullArt trim/crop marker in the prepared output.");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
