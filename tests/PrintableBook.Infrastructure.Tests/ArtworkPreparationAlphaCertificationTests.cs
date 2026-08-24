using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;

namespace PrintableBook.Infrastructure.Tests;

public sealed class ArtworkPreparationAlphaCertificationTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ArtworkPreparationAlphaTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(ArtworkType.BorderArt)]
    [InlineData(ArtworkType.FullArt)]
    [InlineData(ArtworkType.CropArt)]
    public async Task PrepareAsync_flattens_each_type_specific_path_to_an_opaque_white_2270_png(ArtworkType type)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{type}.source.png");
        var target = Path.Combine(rootPath, $"{type}.prepared.png");
        using (var image = CreateTransparentSource(type))
        {
            image.Write(source);
        }

        var result = await CreateService().PrepareAsync(CreateRequest(source, target, type));

        Assert.Equal(type != ArtworkType.CropArt, result.AutoFrameRecommended);
        using var output = new MagickImage(target);
        Assert.Equal(MagickFormat.Png, output.Format);
        Assert.Equal((uint)2270, output.Width);
        Assert.Equal((uint)2270, output.Height);
        var rgba = output.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException("Expected RGBA pixel data.");
        Assert.All(rgba.Where((_, index) => index % 4 == 3), alpha => Assert.Equal((byte)255, alpha));
        Assert.Equal([255, 255, 255, 255], rgba.Take(4));
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

    private static ArtworkPreparationService CreateService() => new(
        new BorderArtPreparationProcessor(
            new MagickBorderBoundsCropProcessor(),
            new MagickSquareCropProcessor(),
            new MagickArtworkResizeProcessor()),
        new FullArtPreparationProcessor(
            new MagickArtworkTrimProcessor(),
            new MagickSquareCropProcessor(),
            new MagickArtworkResizeProcessor()),
        new CropArtPreparationProcessor(
            new MagickArtworkTrimProcessor(),
            new MagickSquarePadProcessor(),
            new MagickArtworkResizeProcessor()),
        new MagickImageInspector());

    private static ArtworkPreparationRequest CreateRequest(string source, string target, ArtworkType type) =>
        new(
            new FileReference(source),
            new FileReference(target),
            CreateClassification(type),
            new ArtworkDetectionThreshold(20),
            new ImageSize(2270, 2270),
            new ImageDensity(300, 300));

    private static ArtworkClassificationResult CreateClassification(ArtworkType type) =>
        type switch
        {
            ArtworkType.BorderArt => new ArtworkClassificationResult(
                ArtworkType.BorderArt,
                BorderLineDetectionResult.Detected(
                    BorderLineSideResult.Detected(5),
                    BorderLineSideResult.Detected(34),
                    BorderLineSideResult.Detected(5),
                    BorderLineSideResult.Detected(24),
                    new ImageRectangle(new ImagePoint(5, 5), new ImageSize(30, 20))),
                null),
            ArtworkType.FullArt => new ArtworkClassificationResult(
                ArtworkType.FullArt,
                BorderLineDetectionResult.NoBorder(),
                BorderPixelDetectionResult.Detected(true, false, false, false)),
            ArtworkType.CropArt => new ArtworkClassificationResult(
                ArtworkType.CropArt,
                BorderLineDetectionResult.NoBorder(),
                BorderPixelDetectionResult.None()),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

    private static MagickImage CreateTransparentSource(ArtworkType type)
    {
        var image = new MagickImage(MagickColors.White, 40, 30);
        image.Alpha(AlphaOption.On);
        var pixels = image.GetPixels();
        for (var y = 0; y < 30; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                pixels.SetPixel(x, y, [255, 255, 255, 0]);
            }
        }

        if (type == ArtworkType.BorderArt)
        {
            PaintRectangleOutline(pixels, 5, 5, 34, 24, [0, 0, 0, 255]);
            Fill(pixels, 14, 10, 10, 10, [0, 0, 0, 255]);
        }
        else
        {
            pixels.SetPixel(10, 10, [0, 0, 0, 255]);
            pixels.SetPixel(29, 19, [0, 0, 0, 255]);
            Fill(pixels, 16, 12, 8, 6, [0, 0, 0, 255]);
        }

        return image;
    }

    private static void PaintRectangleOutline(IPixelCollection<byte> pixels, int left, int top, int right, int bottom, byte[] color)
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
}
