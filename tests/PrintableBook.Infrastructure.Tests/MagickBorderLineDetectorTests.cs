using ImageMagick;
using System.Text.RegularExpressions;
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
        Assert.Null(result.BorderBounds);
    }

    [Fact]
    public async Task DetectAsync_accepts_a_local_gap_inside_an_otherwise_persistent_outer_track()
    {
        var source = WriteBorderedImage("white-pixel", 1024, 1024, 20, 1003, 15, 1008, pixels =>
            pixels.SetPixel(20, 512, [255, 255, 255, 255]));

        var result = await DetectAsync(source);

        AssertBorder(result, 20, 1003, 15, 1008);
    }

    [Fact]
    public async Task DetectAsync_honors_exact_threshold_without_treating_one_local_noise_pixel_as_a_missing_frame()
    {
        var accepted = WriteBorderedImage("threshold-accepted", 1024, 1024, 20, 1003, 15, 1008,
            pixels => DrawBorder(pixels, 1024, 1024, 20, 1003, 15, 1008, 20, 20, 20, 255));
        var rejected = WriteBorderedImage("threshold-rejected", 1024, 1024, 20, 1003, 15, 1008,
            pixels => pixels.SetPixel(20, 512, [21, 20, 20, 255]));

        AssertBorder(await DetectAsync(accepted), 20, 1003, 15, 1008);
        AssertBorder(await DetectAsync(rejected), 20, 1003, 15, 1008);
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
    public async Task DetectAsync_rejects_a_vertical_edge_line_that_is_not_continuous_through_the_center_sample()
    {
        var source = WriteImage("partial-vertical-edge", 1024, 1024, pixels =>
        {
            DrawVerticalLine(pixels, 20, 1024, 0, 0, 0, 255, 0, 200);
            DrawVerticalLine(pixels, 20, 1024, 0, 0, 0, 255, 824, 1023);
            DrawVerticalLine(pixels, 1003, 1024, 0, 0, 0, 255);
            DrawHorizontalLine(pixels, 15, 1024, 0, 0, 0, 255);
            DrawHorizontalLine(pixels, 1008, 1024, 0, 0, 0, 255);
        });

        var result = await DetectAsync(source);

        Assert.False(result.HasBorder);
        Assert.False(result.Left.Found);
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
    public async Task DetectAsync_accepts_multiple_interrupted_outer_border_segments()
    {
        var source = WriteBorderedImage("multiple-gaps", 1024, 1024, 40, 983, 40, 983, pixels =>
        {
            EraseVerticalRange(pixels, 40, 180, 235);
            EraseVerticalRange(pixels, 40, 540, 595);
            EraseVerticalRange(pixels, 983, 340, 400);
            EraseHorizontalRange(pixels, 40, 420, 475);
            EraseHorizontalRange(pixels, 983, 650, 705);
        });

        AssertBorder(await DetectAsync(source), 40, 983, 40, 983);
    }

    [Fact]
    public async Task DetectAsync_accepts_a_rounded_frame_when_only_the_non_corner_track_is_visible()
    {
        var source = WriteImage("rounded-frame", 1024, 1024, pixels =>
        {
            DrawVerticalLine(pixels, 40, 1024, 0, 0, 0, 255, 102, 921);
            DrawVerticalLine(pixels, 983, 1024, 0, 0, 0, 255, 102, 921);
            DrawHorizontalLine(pixels, 40, 1024, 0, 0, 0, 255, 102, 921);
            DrawHorizontalLine(pixels, 983, 1024, 0, 0, 0, 255, 102, 921);
        });

        AssertBorder(await DetectAsync(source), 40, 983, 40, 983);
    }

    [Fact]
    public async Task DetectAsync_accepts_gradually_moving_outer_side_tracks()
    {
        var source = WriteImage("moving-tracks", 1024, 1024, pixels =>
        {
            for (var coordinate = 0; coordinate < 1024; coordinate++)
            {
                var offset = (coordinate / 128) - 4;
                pixels.SetPixel(40 + offset, coordinate, [0, 0, 0, 255]);
                pixels.SetPixel(983 - offset, coordinate, [0, 0, 0, 255]);
                pixels.SetPixel(coordinate, 40 + offset, [0, 0, 0, 255]);
                pixels.SetPixel(coordinate, 983 - offset, [0, 0, 0, 255]);
            }
        });

        var result = await DetectAsync(source);

        Assert.True(result.HasBorder);
        Assert.InRange(result.Left.Position!.Value, 36, 44);
        Assert.InRange(result.Right.Position!.Value, 979, 987);
        Assert.InRange(result.Top.Position!.Value, 36, 44);
        Assert.InRange(result.Bottom.Position!.Value, 979, 987);
    }

    [Fact]
    public async Task DetectAsync_selects_a_gapped_outer_frame_over_a_stronger_inner_rectangle()
    {
        var source = WriteImage("gapped-outer-stronger-inner", 1024, 1024, pixels =>
        {
            DrawBorder(pixels, 1024, 1024, 40, 983, 40, 983, 0, 0, 0, 255);
            EraseVerticalRange(pixels, 40, 240, 300);
            EraseVerticalRange(pixels, 983, 700, 760);
            EraseHorizontalRange(pixels, 40, 510, 570);
            EraseHorizontalRange(pixels, 983, 350, 410);
            DrawThickBorder(pixels, 1024, 1024, 80, 943, 80, 943, 3);
        });

        AssertBorder(await DetectAsync(source), 40, 983, 40, 983);
    }

    [Fact]
    public async Task DetectAsync_selects_outer_frame_over_bookshelf_and_window_like_lines()
    {
        var source = WriteImage("outer-with-interior-structures", 1024, 1024, pixels =>
        {
            DrawBorder(pixels, 1024, 1024, 30, 993, 30, 993, 0, 0, 0, 255);
            DrawVerticalLine(pixels, 72, 1024, 0, 0, 0, 255, 110, 913);
            DrawHorizontalLine(pixels, 72, 1024, 0, 0, 0, 255, 110, 913);
            DrawVerticalLine(pixels, 120, 1024, 0, 0, 0, 255, 280, 760);
            DrawHorizontalLine(pixels, 120, 1024, 0, 0, 0, 255, 280, 760);
        });

        AssertBorder(await DetectAsync(source), 30, 993, 30, 993);
    }

    [Fact]
    public async Task DetectAsync_rejects_unrelated_internal_lines_inside_the_outer_corridor()
    {
        var source = WriteImage("unrelated-corridor-lines", 1024, 1024, pixels =>
        {
            DrawAlternatingSegments(pixels, vertical: true, firstDepth: 20, secondDepth: 80, length: 1024, fromFarEdge: false);
            DrawAlternatingSegments(pixels, vertical: true, firstDepth: 20, secondDepth: 80, length: 1024, fromFarEdge: true);
            DrawAlternatingSegments(pixels, vertical: false, firstDepth: 20, secondDepth: 80, length: 1024, fromFarEdge: false);
            DrawAlternatingSegments(pixels, vertical: false, firstDepth: 20, secondDepth: 80, length: 1024, fromFarEdge: true);
        });

        Assert.False((await DetectAsync(source)).HasBorder);
    }

    [Fact]
    public async Task DetectAsync_rejects_four_side_tracks_that_do_not_enter_the_corner_rois()
    {
        var source = WriteImage("disconnected-corner-tracks", 1024, 1024, pixels =>
        {
            DrawVerticalLine(pixels, 40, 1024, 0, 0, 0, 255, 150, 873);
            DrawVerticalLine(pixels, 983, 1024, 0, 0, 0, 255, 150, 873);
            DrawHorizontalLine(pixels, 40, 1024, 0, 0, 0, 255, 150, 873);
            DrawHorizontalLine(pixels, 983, 1024, 0, 0, 0, 255, 150, 873);
        });

        Assert.False((await DetectAsync(source)).HasBorder);
    }

    [Fact]
    public async Task DetectAsync_rejects_objects_that_touch_only_one_or_two_outer_sides()
    {
        var source = WriteImage("edge-touching-objects", 1024, 1024, pixels =>
        {
            DrawVerticalLine(pixels, 20, 1024, 0, 0, 0, 255, 120, 903);
            DrawHorizontalLine(pixels, 20, 1024, 0, 0, 0, 255, 120, 903);
            DrawVerticalLine(pixels, 70, 1024, 0, 0, 0, 255, 300, 723);
            DrawHorizontalLine(pixels, 70, 1024, 0, 0, 0, 255, 300, 723);
        });

        Assert.False((await DetectAsync(source)).HasBorder);
    }

    [Fact]
    public async Task DetectAsync_accepts_outer_tracks_with_local_gray_noise_and_artwork_occlusion()
    {
        var source = WriteBorderedImage("noise-and-occlusion", 1024, 1024, 35, 988, 35, 988, pixels =>
        {
            EraseVerticalRange(pixels, 35, 450, 500);
            EraseHorizontalRange(pixels, 988, 600, 650);
            for (var index = 150; index < 875; index += 37)
            {
                pixels.SetPixel(36, index, [21, 21, 21, 255]);
                pixels.SetPixel(index, 36, [21, 21, 21, 255]);
            }
        });

        AssertBorder(await DetectAsync(source), 35, 988, 35, 988);
    }

    [Fact]
    public async Task MeasureAsync_returns_segment_and_corner_evidence_for_the_selected_outer_frame()
    {
        var source = WriteBorderedImage("measurement-evidence", 1024, 1024, 40, 983, 40, 983);

        var measurement = await new MagickBorderLineDetector().MeasureAsync(
            new BorderLineDetectionRequest(new FileReference(source), new ArtworkDetectionThreshold(20)));

        AssertBorder(measurement.Detection, 40, 983, 40, 983);
        Assert.All(
            new[] { measurement.Left, measurement.Right, measurement.Top, measurement.Bottom },
            side => Assert.Contains(side.Candidates, candidate => candidate.SupportedSegments == 8));
        Assert.Equal(4, measurement.CornerEvidence.Count);
        Assert.All(measurement.CornerEvidence, corner => Assert.True(corner.HasOuterInkEvidence));
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
        Assert.Contains("pixels.ToByteArray(", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"ToByteArray\(").Cast<Match>());
        Assert.Equal(4, Regex.Matches(source, @"var (left|right|top|bottom) = MeasureSide\(").Count);
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

    private static void EraseVerticalRange(IPixelCollection<byte> pixels, int x, int startY, int endY) =>
        DrawVerticalLine(pixels, x, endY + 1, 255, 255, 255, 255, startY, endY);

    private static void EraseHorizontalRange(IPixelCollection<byte> pixels, int y, int startX, int endX) =>
        DrawHorizontalLine(pixels, y, endX + 1, 255, 255, 255, 255, startX, endX);

    private static void DrawThickBorder(
        IPixelCollection<byte> pixels,
        int width,
        int height,
        int left,
        int right,
        int top,
        int bottom,
        int thickness)
    {
        for (var offset = 0; offset < thickness; offset++)
        {
            DrawBorder(pixels, width, height, left + offset, right - offset, top + offset, bottom - offset, 0, 0, 0, 255);
        }
    }

    private static void DrawAlternatingSegments(
        IPixelCollection<byte> pixels,
        bool vertical,
        int firstDepth,
        int secondDepth,
        int length,
        bool fromFarEdge)
    {
        const int segments = 8;
        for (var segment = 0; segment < segments; segment++)
        {
            var start = segment * length / segments;
            var end = ((segment + 1) * length / segments) - 1;
            var depth = segment % 2 == 0 ? firstDepth : secondDepth;
            var coordinate = fromFarEdge ? length - 1 - depth : depth;
            if (vertical)
            {
                DrawVerticalLine(pixels, coordinate, length, 0, 0, 0, 255, start, end);
            }
            else
            {
                DrawHorizontalLine(pixels, coordinate, length, 0, 0, 0, 255, start, end);
            }
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
