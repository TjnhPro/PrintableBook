using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;

namespace PrintableBook.Infrastructure.Tests;

public sealed class ArtworkPreparationServiceTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ArtworkPreparationServiceTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(ArtworkType.BorderArt, true)]
    [InlineData(ArtworkType.FullArt, true)]
    [InlineData(ArtworkType.CropArt, false)]
    public async Task PrepareAsync_writes_an_opaque_2270_square_and_returns_the_locked_frame_policy(ArtworkType type, bool frameAllowed)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{type}.png");
        var target = Path.Combine(rootPath, $"{type}.prepared.png");
        using (var image = CreateSource(type))
        {
            image.Write(source);
        }

        var result = await CreateService().PrepareAsync(CreateRequest(source, target, type));

        Assert.Equal(new FileReference(target), result.File);
        Assert.Equal(type, result.Type);
        Assert.Equal(frameAllowed, result.FrameAllowed);
        using var output = new MagickImage(target);
        Assert.Equal((uint)2270, output.Width);
        Assert.Equal((uint)2270, output.Height);
        AssertOpaque(output);
    }

    [Fact]
    public async Task PrepareAsync_rejects_non_square_prepared_geometry()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "full.png");
        using (var image = CreateSource(ArtworkType.FullArt))
        {
            image.Write(source);
        }

        var request = CreateRequest(source, Path.Combine(rootPath, "target.png"), ArtworkType.FullArt) with
        {
            PreparedArtworkSize = new ImageSize(2270, 2200)
        };

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().PrepareAsync(request).AsTask());
    }

    [Fact]
    public async Task PrepareAsync_rejects_borderart_without_border_bounds()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "border.png");
        using (var image = CreateSource(ArtworkType.BorderArt))
        {
            image.Write(source);
        }

        var invalidClassification = new ArtworkClassificationResult(
            ArtworkType.BorderArt,
            new BorderLineDetectionResult(
                true,
                BorderLineSideResult.Detected(5),
                BorderLineSideResult.Detected(34),
                BorderLineSideResult.Detected(5),
                BorderLineSideResult.Detected(24),
                null),
            null);
        var request = CreateRequest(source, Path.Combine(rootPath, "target.png"), ArtworkType.BorderArt) with
        {
            Classification = invalidClassification
        };

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().PrepareAsync(request).AsTask());
    }

    [Fact]
    public async Task PrepareAsync_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = CreateRequest("source.png", Path.Combine(rootPath, "target.png"), ArtworkType.CropArt);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService().PrepareAsync(request, cancellation.Token).AsTask());
    }

    [Theory]
    [InlineData(ArtworkType.BorderArt)]
    [InlineData(ArtworkType.FullArt)]
    [InlineData(ArtworkType.CropArt)]
    public async Task PrepareAsync_propagates_missing_source_without_publishing_output(ArtworkType type)
    {
        Directory.CreateDirectory(rootPath);
        var target = Path.Combine(rootPath, $"{type}.missing-source.prepared.png");

        await Assert.ThrowsAnyAsync<MagickException>(() => CreateService().PrepareAsync(
            CreateRequest(Path.Combine(rootPath, "missing.png"), target, type)).AsTask());

        Assert.False(File.Exists(target));
    }

    [Theory]
    [InlineData(ArtworkType.BorderArt)]
    [InlineData(ArtworkType.FullArt)]
    [InlineData(ArtworkType.CropArt)]
    public async Task PrepareAsync_propagates_corrupt_source_without_publishing_output(ArtworkType type)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{type}.corrupt.png");
        var target = Path.Combine(rootPath, $"{type}.corrupt.prepared.png");
        await File.WriteAllTextAsync(source, "not a PNG");

        await Assert.ThrowsAnyAsync<MagickException>(() => CreateService().PrepareAsync(
            CreateRequest(source, target, type)).AsTask());

        Assert.False(File.Exists(target));
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

    private static MagickImage CreateSource(ArtworkType type)
    {
        if (type == ArtworkType.BorderArt)
        {
            var border = new MagickImage(MagickColors.Transparent, 40, 30);
            PaintRectangleOutline(border.GetPixels(), 5, 5, 34, 24, [0, 0, 0, 255]);
            Fill(border.GetPixels(), 14, 10, 10, 10, [0, 200, 0, 255]);
            return border;
        }

        var image = new MagickImage(MagickColors.White, 60, 40);
        var pixels = image.GetPixels();
        if (type == ArtworkType.FullArt)
        {
            pixels.SetPixel(5, 10, [0, 0, 0]);
            pixels.SetPixel(54, 29, [0, 0, 0]);
            Fill(pixels, 25, 15, 10, 10, [0, 200, 0]);
        }
        else
        {
            pixels.SetPixel(10, 10, [0, 0, 0]);
            pixels.SetPixel(49, 29, [0, 0, 0]);
            Fill(pixels, 20, 15, 10, 10, [0, 200, 0]);
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

    private static void AssertOpaque(MagickImage image)
    {
        var rgba = image.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException("Expected RGBA pixel data.");
        Assert.All(rgba.Where((_, index) => index % 4 == 3), alpha => Assert.Equal((byte)255, alpha));
    }
}
