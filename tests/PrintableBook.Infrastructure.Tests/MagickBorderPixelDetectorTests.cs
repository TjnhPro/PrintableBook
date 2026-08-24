using System.Text.RegularExpressions;
using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickBorderPixelDetectorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BorderPixelTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task DetectAsync_returns_no_hits_when_ink_is_inside_a_white_margin()
    {
        var source = WriteImage("inside-margin", 256, 256, pixels =>
            DrawRectangle(pixels, 10, 10, 245, 245, 0, 0, 0, 255));

        AssertNone(await DetectAsync(source));
    }

    [Theory]
    [InlineData(0, 128, true, false, false, false)]
    [InlineData(255, 128, false, true, false, false)]
    [InlineData(128, 0, false, false, true, false)]
    [InlineData(128, 255, false, false, false, true)]
    public async Task DetectAsync_reports_a_single_qualifying_pixel_on_each_exact_edge(
        int x,
        int y,
        bool left,
        bool right,
        bool top,
        bool bottom)
    {
        var source = WriteImage($"single-{x}-{y}", 256, 256, pixels =>
            pixels.SetPixel(x, y, [0, 0, 0, 255]));

        AssertHits(await DetectAsync(source), left, right, top, bottom);
    }

    [Fact]
    public async Task DetectAsync_reports_all_four_sides()
    {
        var source = WriteImage("all-sides", 256, 256, pixels =>
        {
            pixels.SetPixel(0, 128, [0, 0, 0, 255]);
            pixels.SetPixel(255, 128, [0, 0, 0, 255]);
            pixels.SetPixel(128, 0, [0, 0, 0, 255]);
            pixels.SetPixel(128, 255, [0, 0, 0, 255]);
        });

        AssertHits(await DetectAsync(source), true, true, true, true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DetectAsync_rejects_ink_that_is_inside_the_exact_perimeter(int inset)
    {
        var source = WriteImage($"inset-{inset}", 256, 256, pixels =>
        {
            pixels.SetPixel(inset, 128, [0, 0, 0, 255]);
            pixels.SetPixel(255 - inset, 128, [0, 0, 0, 255]);
            pixels.SetPixel(128, inset, [0, 0, 0, 255]);
            pixels.SetPixel(128, 255 - inset, [0, 0, 0, 255]);
        });

        AssertNone(await DetectAsync(source));
    }

    [Fact]
    public async Task DetectAsync_accepts_rgb_at_the_configured_threshold()
    {
        var source = WriteImage("threshold", 256, 256, pixels =>
            pixels.SetPixel(0, 128, [20, 20, 20, 255]));

        AssertHits(await DetectAsync(source), true, false, false, false);
    }

    [Fact]
    public async Task DetectAsync_rejects_rgb_just_above_the_configured_threshold()
    {
        var source = WriteImage("threshold-plus-one", 256, 256, pixels =>
            pixels.SetPixel(0, 128, [21, 20, 20, 255]));

        AssertNone(await DetectAsync(source));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(127, false)]
    [InlineData(128, true)]
    public async Task DetectAsync_honors_the_visible_alpha_boundary(byte alpha, bool expectedHit)
    {
        var source = WriteImage($"alpha-{alpha}", 256, 256, pixels =>
            pixels.SetPixel(0, 128, [0, 0, 0, alpha]));

        var result = await DetectAsync(source);

        Assert.Equal(expectedHit, result.HasBorderPixel);
        Assert.Equal(expectedHit, result.LeftHit);
    }

    [Fact]
    public async Task DetectAsync_rejects_a_strong_internal_rectangle()
    {
        var source = WriteImage("internal-rectangle", 256, 256, pixels =>
            DrawRectangle(pixels, 20, 20, 235, 235, 0, 0, 0, 255));

        AssertNone(await DetectAsync(source));
    }

    [Fact]
    public async Task DetectAsync_counts_a_corner_pixel_for_its_two_touched_sides()
    {
        var source = WriteImage("corner", 256, 256, pixels =>
            pixels.SetPixel(0, 0, [0, 0, 0, 255]));

        AssertHits(await DetectAsync(source), true, false, true, false);
    }

    [Fact]
    public async Task DetectAsync_handles_portrait_and_landscape_rasters()
    {
        var portrait = WriteImage("portrait", 300, 500, pixels =>
            pixels.SetPixel(299, 250, [0, 0, 0, 255]));
        var landscape = WriteImage("landscape", 500, 300, pixels =>
            pixels.SetPixel(250, 299, [0, 0, 0, 255]));

        AssertHits(await DetectAsync(portrait), false, true, false, false);
        AssertHits(await DetectAsync(landscape), false, false, false, true);
    }

    [Fact]
    public async Task DetectAsync_accepts_a_jpeg_perimeter_stroke_that_decodes_as_qualifying_ink()
    {
        var source = WriteImage("jpeg-edge", 256, 256, pixels =>
        {
            for (var y = 80; y < 176; y++)
            {
                pixels.SetPixel(0, y, [0, 0, 0, 255]);
            }
        }, MagickFormat.Jpeg);

        Assert.True((await DetectAsync(source)).LeftHit);
    }

    [Fact]
    public async Task DetectAsync_propagates_corrupt_image_failures()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "corrupt.png");
        await File.WriteAllTextAsync(source, "not a PNG");

        await Assert.ThrowsAnyAsync<MagickException>(() => DetectAsync(source).AsTask());
    }

    [Fact]
    public async Task DetectAsync_honors_cancellation_before_image_decode()
    {
        var source = WriteImage("cancelled", 256, 256, _ => { });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MagickBorderPixelDetector().DetectAsync(
            new BorderPixelDetectionRequest(new FileReference(source), new ArtworkDetectionThreshold(20)), cancellation.Token).AsTask());
    }

    [Fact]
    public void Detector_source_uses_one_decode_and_bounded_rgba_perimeter_reads()
    {
        var sourcePath = FindRepositoryFile(Path.Combine("src", "PrintableBook.Infrastructure", "Imaging", "MagickBorderPixelDetector.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("using var image = new MagickImage(request.Source.Value);", source, StringComparison.Ordinal);
        Assert.Contains("using var pixels = image.GetPixels();", source, StringComparison.Ordinal);
        Assert.Contains("pixels.ToByteArray(", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"ToByteArray\(").Cast<Match>());
        Assert.DoesNotContain("GetPixel(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValue(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Parallel.", source, StringComparison.Ordinal);
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(rootPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private ValueTask<BorderPixelDetectionResult> DetectAsync(string source) =>
        new MagickBorderPixelDetector().DetectAsync(
            new BorderPixelDetectionRequest(new FileReference(source), new ArtworkDetectionThreshold(20)));

    private string WriteImage(
        string id,
        int width,
        int height,
        Action<IPixelCollection<byte>> paint,
        MagickFormat format = MagickFormat.Png)
    {
        var extension = format == MagickFormat.Jpeg ? "jpg" : "png";
        var path = Path.Combine(rootPath, $"{id}.{extension}");
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        image.Alpha(AlphaOption.On);
        paint(image.GetPixels());
        image.Format = format;
        image.Write(path);
        return path;
    }

    private static void DrawRectangle(
        IPixelCollection<byte> pixels,
        int left,
        int top,
        int right,
        int bottom,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        for (var x = left; x <= right; x++)
        {
            pixels.SetPixel(x, top, [red, green, blue, alpha]);
            pixels.SetPixel(x, bottom, [red, green, blue, alpha]);
        }

        for (var y = top; y <= bottom; y++)
        {
            pixels.SetPixel(left, y, [red, green, blue, alpha]);
            pixels.SetPixel(right, y, [red, green, blue, alpha]);
        }
    }

    private static void AssertNone(BorderPixelDetectionResult result) =>
        AssertHits(result, false, false, false, false);

    private static void AssertHits(
        BorderPixelDetectionResult result,
        bool left,
        bool right,
        bool top,
        bool bottom)
    {
        Assert.Equal(left || right || top || bottom, result.HasBorderPixel);
        Assert.Equal(left, result.LeftHit);
        Assert.Equal(right, result.RightHit);
        Assert.Equal(top, result.TopHit);
        Assert.Equal(bottom, result.BottomHit);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Expected repository file '{relativePath}'.");
    }
}
