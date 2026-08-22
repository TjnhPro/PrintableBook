using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

/// <summary>
/// Pixel-level certification corpus for trimming raw artwork before any resize or page composition occurs.
/// Each case writes and reopens a real PNG; no image processor is mocked.
/// </summary>
public sealed class MagickArtworkTrimCertificationTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.TrimCertification.{Guid.NewGuid():N}");

    public static IEnumerable<object[]> KnownBoundsCases()
    {
        foreach (var fixture in new[]
        {
            new TrimFixture("normal-white-margin", 1000, 900, 111, 73, 788, 700, 0),
            new TrimFixture("large-white-margin", 1200, 1100, 300, 240, 600, 500, 0),
            new TrimFixture("small-white-margin", 240, 180, 2, 3, 235, 172, 0),
            new TrimFixture("left-heavy-margin", 800, 700, 240, 40, 450, 610, 0),
            new TrimFixture("right-heavy-margin", 800, 700, 30, 40, 450, 610, 0),
            new TrimFixture("top-heavy-margin", 800, 700, 60, 250, 650, 400, 0),
            new TrimFixture("bottom-heavy-margin", 800, 700, 60, 30, 650, 400, 0),
            new TrimFixture("portrait-artwork", 900, 1100, 120, 100, 500, 860, 0),
            new TrimFixture("landscape-artwork", 1100, 900, 100, 160, 860, 480, 0),
            new TrimFixture("square-artwork", 900, 900, 170, 170, 560, 560, 0),
            new TrimFixture("thin-lines", 900, 700, 44, 55, 790, 590, 0),
            new TrimFixture("thick-lines", 900, 700, 120, 90, 660, 510, 0),
            new TrimFixture("near-black", 900, 700, 80, 60, 720, 540, 20),
            new TrimFixture("anti-aliased-lines", 900, 700, 90, 75, 700, 520, 20),
            new TrimFixture("artwork-near-edge", 900, 700, 0, 1, 899, 698, 0),
            new TrimFixture("complex-line-art", 1000, 800, 77, 66, 840, 650, 0),
            new TrimFixture("almost-empty", 500, 400, 249, 199, 2, 2, 0),
            new TrimFixture("wide-thin", 1000, 600, 20, 290, 960, 2, 0),
            new TrimFixture("tall-thin", 600, 1000, 299, 20, 2, 960, 0),
            new TrimFixture("odd-bounds", 901, 799, 101, 103, 697, 593, 0)
        })
        {
            yield return [fixture];
        }
    }

    [Theory]
    [MemberData(nameof(KnownBoundsCases))]
    public async Task TrimAsync_crops_each_deterministic_artwork_fixture_to_its_exact_known_bounds(TrimFixture fixture)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{fixture.Id}.input.png");
        var target = Path.Combine(rootPath, $"{fixture.Id}.trim.png");
        WriteArtworkFixture(source, fixture);

        var result = await new MagickArtworkTrimProcessor().TrimAsync(new ArtworkTrimRequest(
            new FileReference(source),
            new FileReference(target),
            new ArtworkDetectionThreshold(fixture.Threshold)));

        var expectedBounds = new ImageRectangle(
            new ImagePoint(fixture.X, fixture.Y),
            new ImageSize(fixture.Width, fixture.Height));
        Assert.True(result.HasArtwork);
        Assert.Equal(expectedBounds, result.ArtworkBounds);
        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(expectedBounds.Size, info.Size);
        using var output = new MagickImage(target);
        Assert.InRange(output.GetPixels().GetPixel(0, 0)[0], (byte)0, fixture.Threshold);
        Assert.InRange(output.GetPixels().GetPixel(fixture.Width - 1, fixture.Height - 1)[0], (byte)0, fixture.Threshold);
    }

    [Fact]
    public async Task TrimAsync_returns_no_artwork_for_an_all_white_real_png()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "all-white.input.png");
        using (var image = new MagickImage(MagickColors.White, 400, 300))
        {
            image.Write(source);
        }

        var target = new FileReference(Path.Combine(rootPath, "all-white.trim.png"));
        var result = await new MagickArtworkTrimProcessor().TrimAsync(new ArtworkTrimRequest(
            new FileReference(source), target, new ArtworkDetectionThreshold(20)));

        Assert.False(result.HasArtwork);
        Assert.False(File.Exists(target.Value));
    }

    [Fact]
    public async Task TrimAsync_fails_for_a_corrupt_png_without_publishing_an_output()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "corrupt.input.png");
        await File.WriteAllTextAsync(source, "not a PNG");
        var target = Path.Combine(rootPath, "corrupt.trim.png");

        await Assert.ThrowsAnyAsync<MagickException>(() => new MagickArtworkTrimProcessor().TrimAsync(new ArtworkTrimRequest(
            new FileReference(source), new FileReference(target), new ArtworkDetectionThreshold(20))).AsTask());

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

    private static void WriteArtworkFixture(string path, TrimFixture fixture)
    {
        using var image = new MagickImage(MagickColors.White, (uint)fixture.CanvasWidth, (uint)fixture.CanvasHeight);
        var pixels = image.GetPixels();
        var shade = fixture.Threshold == 0 ? (byte)0 : fixture.Threshold;
        for (var x = fixture.X; x < fixture.X + fixture.Width; x++)
        {
            pixels.SetPixel(x, fixture.Y, [shade, shade, shade]);
            pixels.SetPixel(x, fixture.Y + fixture.Height - 1, [0, 0, 0]);
        }

        for (var y = fixture.Y; y < fixture.Y + fixture.Height; y++)
        {
            pixels.SetPixel(fixture.X, y, [0, 0, 0]);
            pixels.SetPixel(fixture.X + fixture.Width - 1, y, [shade, shade, shade]);
        }

        pixels.SetPixel(fixture.X + fixture.Width / 2, fixture.Y + fixture.Height / 2, [0, 0, 0]);
        image.Write(path);
    }

    public sealed record TrimFixture(string Id, int CanvasWidth, int CanvasHeight, int X, int Y, int Width, int Height, byte Threshold);
}
