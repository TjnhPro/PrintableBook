using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickArtworkResizeCertificationTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ResizeCertification.{Guid.NewGuid():N}");

    public static IEnumerable<object[]> Cases() =>
    [
        ["square", 1000, 1000, 2270, 2270],
        ["portrait", 800, 1000, 1816, 2270],
        ["landscape", 1000, 800, 2270, 1816],
        ["exact-max-side", 2270, 1800, 2270, 1800],
        ["small-upscale", 400, 300, 2270, 1703],
        ["large-downscale", 4000, 3000, 2270, 1703],
        ["rounding-sensitive", 788, 900, 1988, 2270]
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ResizeAsync_scales_real_artwork_proportionally_to_the_configured_maximum_side(
        string id, int width, int height, int expectedWidth, int expectedHeight)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{id}.input.png");
        var target = Path.Combine(rootPath, $"{id}.resize.png");
        using (var image = new MagickImage(MagickColors.White, (uint)width, (uint)height))
        {
            for (var y = height / 2 - 4; y <= height / 2 + 4; y++)
            {
                for (var x = width / 2 - 4; x <= width / 2 + 4; x++)
                {
                    image.GetPixels().SetPixel(x, y, [0, 0, 0]);
                }
            }
            image.Write(source);
        }

        await new MagickArtworkResizeProcessor().ResizeAsync(new ArtworkResizeRequest(
            new FileReference(source), new FileReference(target), 2270, new ImageDensity(300, 300)));

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(target));
        Assert.Equal(new ImageSize(expectedWidth, expectedHeight), info.Size);
        Assert.Equal(2270, Math.Max(info.Size.Width, info.Size.Height));
        Assert.Equal(300, info.Density!.Value.Horizontal, precision: 2);
        using var output = new MagickImage(target);
        Assert.True(HasDarkPixelNear(output, expectedWidth / 2, expectedHeight / 2));
        CertificationArtifactStore.Capture("resize", id, source, target);
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

    private static bool HasDarkPixelNear(MagickImage image, int centerX, int centerY)
    {
        var pixels = image.GetPixels();
        for (var y = centerY - 3; y <= centerY + 3; y++)
        {
            for (var x = centerX - 3; x <= centerX + 3; x++)
            {
                if (pixels.GetPixel(x, y)[0] < 128)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
