using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickBorderLineDetectorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BorderLineTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task DetectAsync_returns_exact_geometry_for_a_1024_square_border()
    {
        var source = WriteBorderedImage("square", 1024, 1024, 20, 1003, 15, 1008);

        var result = await DetectAsync(source);

        AssertBorder(result, 20, 1003, 15, 1008);
    }

    [Fact]
    public async Task DetectAsync_accepts_lines_at_the_100_pixel_search_boundary()
    {
        var source = WriteBorderedImage("boundary", 1024, 1024, 100, 923, 100, 923);

        var result = await DetectAsync(source);

        AssertBorder(result, 100, 923, 100, 923);
    }

    [Fact]
    public async Task DetectAsync_rejects_a_line_just_outside_the_search_depth()
    {
        var source = WriteBorderedImage("outside", 1024, 1024, 101, 1003, 15, 1008);

        var result = await DetectAsync(source);

        Assert.False(result.HasBorder);
        Assert.False(result.Left.Found);
    }

    [Fact]
    public async Task DetectAsync_rejects_a_missing_bottom_line()
    {
        var source = WriteImage("missing-bottom", 1024, 1024, pixels =>
        {
            DrawVerticalLine(pixels, 20, 1024, 0, 0, 0, 255);
            DrawVerticalLine(pixels, 1003, 1024, 0, 0, 0, 255);
            DrawHorizontalLine(pixels, 15, 1024, 0, 0, 0, 255);
        });

        var result = await DetectAsync(source);

        Assert.False(result.HasBorder);
        Assert.True(result.Left.Found);
        Assert.True(result.Right.Found);
        Assert.True(result.Top.Found);
        Assert.False(result.Bottom.Found);
    }

    [Fact]
    public async Task DetectAsync_rejects_one_white_pixel_inside_a_sampled_vertical_line()
    {
        var source = WriteBorderedImage("white-pixel", 1024, 1024, 20, 1003, 15, 1008, pixels =>
            pixels.SetPixel(20, 512, [255, 255, 255, 255]));

        var result = await DetectAsync(source);

        Assert.False(result.HasBorder);
        Assert.False(result.Left.Found);
    }

    [Fact]
    public async Task DetectAsync_honors_exact_threshold_and_rejects_one_value_above_it()
    {
        var accepted = WriteBorderedImage("threshold-accepted", 1024, 1024, 20, 1003, 15, 1008,
            pixels => DrawBorder(pixels, 1024, 1024, 20, 1003, 15, 1008, 20, 20, 20, 255));
        var rejected = WriteBorderedImage("threshold-rejected", 1024, 1024, 20, 1003, 15, 1008,
            pixels => pixels.SetPixel(20, 512, [21, 20, 20, 255]));

        AssertBorder(await DetectAsync(accepted), 20, 1003, 15, 1008);
        Assert.False((await DetectAsync(rejected)).HasBorder);
    }

    [Fact]
    public async Task DetectAsync_accepts_near_black_rgb_values_at_the_threshold()
    {
        var source = WriteBorderedImage("near-black", 1024, 1024, 20, 1003, 15, 1008,
            pixels => DrawBorder(pixels, 1024, 1024, 20, 1003, 15, 1008, 20, 20, 20, 255));

        AssertBorder(await DetectAsync(source), 20, 1003, 15, 1008);
    }

    [Fact]
    public async Task DetectAsync_rejects_transparent_black_lines()
    {
        var source = WriteImage("transparent-black", 1024, 1024, pixels =>
            DrawBorder(pixels, 1024, 1024, 20, 1003, 15, 1008, 0, 0, 0, 0));

        var result = await DetectAsync(source);

        Assert.False(result.HasBorder);
    }

    [Fact]
    public async Task DetectAsync_requires_alpha_128_or_greater()
    {
        var alpha127 = WriteImage("alpha-127", 1024, 1024, pixels =>
            DrawBorder(pixels, 1024, 1024, 20, 1003, 15, 1008, 0, 0, 0, 127));
        var alpha128 = WriteImage("alpha-128", 1024, 1024, pixels =>
            DrawBorder(pixels, 1024, 1024, 20, 1003, 15, 1008, 0, 0, 0, 128));

        Assert.False((await DetectAsync(alpha127)).HasBorder);
        AssertBorder(await DetectAsync(alpha128), 20, 1003, 15, 1008);
    }

    [Fact]
    public async Task DetectAsync_rejects_strong_interior_geometry_without_an_outer_border()
    {
        var source = WriteImage("interior-geometry", 1024, 1024, pixels =>
        {
            DrawVerticalLine(pixels, 200, 1024, 0, 0, 0, 255, 200, 823);
            DrawVerticalLine(pixels, 823, 1024, 0, 0, 0, 255, 200, 823);
            DrawHorizontalLine(pixels, 200, 1024, 0, 0, 0, 255, 200, 823);
            DrawHorizontalLine(pixels, 823, 1024, 0, 0, 0, 255, 200, 823);
        });

        Assert.False((await DetectAsync(source)).HasBorder);
    }

    [Fact]
    public async Task DetectAsync_rejects_partial_top_and_bottom_edge_segments()
    {
        var source = WriteImage("partial-edges", 1024, 1024, pixels =>
        {
            DrawHorizontalLine(pixels, 15, 1024, 0, 0, 0, 255, 0, 200);
            DrawHorizontalLine(pixels, 1008, 1024, 0, 0, 0, 255, 0, 200);
        });

        Assert.False((await DetectAsync(source)).HasBorder);
    }

    [Fact]
    public async Task DetectAsync_handles_a_400_pixel_square_with_clamped_samples()
    {
        var source = WriteBorderedImage("small-square", 400, 400, 10, 389, 12, 387);

        AssertBorder(await DetectAsync(source), 10, 389, 12, 387);
    }

    [Fact]
    public async Task DetectAsync_rejects_internal_art_on_an_80_pixel_image()
    {
        var source = WriteImage("tiny-internal", 80, 80, pixels =>
        {
            DrawVerticalLine(pixels, 10, 80, 0, 0, 0, 255, 10, 69);
            DrawVerticalLine(pixels, 69, 80, 0, 0, 0, 255, 10, 69);
            DrawHorizontalLine(pixels, 10, 80, 0, 0, 0, 255, 10, 69);
            DrawHorizontalLine(pixels, 69, 80, 0, 0, 0, 255, 10, 69);
        });

        Assert.False((await DetectAsync(source)).HasBorder);
    }

    [Fact]
    public async Task DetectAsync_returns_exact_geometry_for_portrait_images()
    {
        var source = WriteBorderedImage("portrait", 800, 1200, 12, 789, 30, 1169);

        AssertBorder(await DetectAsync(source), 12, 789, 30, 1169);
    }

    [Fact]
    public async Task DetectAsync_returns_exact_geometry_for_landscape_images()
    {
        var source = WriteBorderedImage("landscape", 1200, 800, 25, 1170, 12, 789);

        AssertBorder(await DetectAsync(source), 25, 1170, 12, 789);
    }

    [Fact]
    public async Task DetectAsync_prefers_outermost_candidate_on_each_side()
    {
        var source = WriteImage("multiple-candidates", 1200, 800, pixels =>
        {
            DrawBorder(pixels, 1200, 800, 10, 1179, 10, 789, 0, 0, 0, 255);
            DrawBorder(pixels, 1200, 800, 20, 1169, 20, 779, 0, 0, 0, 255);
        });

        AssertBorder(await DetectAsync(source), 10, 1179, 10, 789);
    }

    [Fact]
    public async Task DetectAsync_honors_cancellation_before_image_decode()
    {
        var source = WriteBorderedImage("cancelled", 400, 400, 10, 389, 10, 389);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MagickBorderLineDetector().DetectAsync(
            new BorderLineDetectionRequest(new FileReference(source), new ArtworkDetectionThreshold(20)), cancellation.Token).AsTask());
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
    public void Detector_source_uses_one_decode_and_roi_exports_without_pixel_by_pixel_magick_reads()
    {
        var sourcePath = FindRepositoryFile(Path.Combine("src", "PrintableBook.Infrastructure", "Imaging", "MagickBorderLineDetector.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("using var image = new MagickImage(request.Source.Value);", source, StringComparison.Ordinal);
        Assert.Contains("using var pixels = image.GetPixels();", source, StringComparison.Ordinal);
        Assert.Contains("ToByteArray(roiX, roiY", source, StringComparison.Ordinal);
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

    private ValueTask<BorderLineDetectionResult> DetectAsync(string source) =>
        new MagickBorderLineDetector().DetectAsync(
            new BorderLineDetectionRequest(new FileReference(source), new ArtworkDetectionThreshold(20)));

    private string WriteBorderedImage(
        string id,
        int width,
        int height,
        int left,
        int right,
        int top,
        int bottom,
        Action<IPixelCollection<byte>>? additionalPixels = null) =>
        WriteImage(id, width, height, pixels =>
        {
            DrawBorder(pixels, width, height, left, right, top, bottom, 0, 0, 0, 255);
            additionalPixels?.Invoke(pixels);
        });

    private string WriteImage(string id, int width, int height, Action<IPixelCollection<byte>> paint)
    {
        var path = Path.Combine(rootPath, $"{id}.png");
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        image.Alpha(AlphaOption.On);
        var pixels = image.GetPixels();
        paint(pixels);
        image.Write(path);
        return path;
    }

    private static void DrawBorder(
        IPixelCollection<byte> pixels,
        int width,
        int height,
        int left,
        int right,
        int top,
        int bottom,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        DrawVerticalLine(pixels, left, height, red, green, blue, alpha);
        DrawVerticalLine(pixels, right, height, red, green, blue, alpha);
        DrawHorizontalLine(pixels, top, width, red, green, blue, alpha);
        DrawHorizontalLine(pixels, bottom, width, red, green, blue, alpha);
    }

    private static void DrawVerticalLine(
        IPixelCollection<byte> pixels, int x, int height, byte red, byte green, byte blue, byte alpha, int startY = 0, int? endY = null)
    {
        for (var y = startY; y <= (endY ?? height - 1); y++)
        {
            pixels.SetPixel(x, y, [red, green, blue, alpha]);
        }
    }

    private static void DrawHorizontalLine(
        IPixelCollection<byte> pixels, int y, int width, byte red, byte green, byte blue, byte alpha, int startX = 0, int? endX = null)
    {
        for (var x = startX; x <= (endX ?? width - 1); x++)
        {
            pixels.SetPixel(x, y, [red, green, blue, alpha]);
        }
    }

    private static void AssertBorder(BorderLineDetectionResult result, int left, int right, int top, int bottom)
    {
        Assert.True(result.HasBorder);
        Assert.Equal(left, result.Left.Position);
        Assert.Equal(right, result.Right.Position);
        Assert.Equal(top, result.Top.Position);
        Assert.Equal(bottom, result.Bottom.Position);
        Assert.Equal(
            new ImageRectangle(new ImagePoint(left, top), new ImageSize(right - left + 1, bottom - top + 1)),
            result.BorderBounds);
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
