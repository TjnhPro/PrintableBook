using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickWorkingPageCertificationTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.WorkingPageCertification.{Guid.NewGuid():N}");

    [Theory]
    [InlineData("square", 2270, 2270, 140, 140)]
    [InlineData("portrait", 1816, 2270, 367, 140)]
    [InlineData("landscape", 2270, 1816, 140, 367)]
    [InlineData("odd-margin", 1815, 2270, 367, 140)]
    public async Task CenterAsync_places_known_artwork_markers_at_exact_floor_center_offsets(string id, int width, int height, int expectedX, int expectedY)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{id}.input.png");
        var target = Path.Combine(rootPath, $"{id}.working.png");
        using (var artwork = new MagickImage(MagickColors.White, (uint)width, (uint)height))
        {
            var pixels = artwork.GetPixels();
            pixels.SetPixel(0, 0, [0, 0, 0]);
            pixels.SetPixel(width - 1, height - 1, [0, 0, 0]);
            pixels.SetPixel(width / 2, height / 2, [0, 0, 0]);
            artwork.Write(source);
        }

        await new MagickWorkingPageProcessor().CenterAsync(new WorkingPageRequest(
            new FileReference(source), new FileReference(target), new ImageSize(2550, 2550)));

        using var page = new MagickImage(target);
        var outputPixels = page.GetPixels();
        Assert.Equal((uint)2550, page.Width);
        Assert.Equal((byte)255, outputPixels.GetPixel(expectedX - 1, expectedY)[0]);
        Assert.Equal((byte)0, outputPixels.GetPixel(expectedX, expectedY)[0]);
        Assert.Equal((byte)0, outputPixels.GetPixel(expectedX + width - 1, expectedY + height - 1)[0]);
        Assert.Equal((byte)255, outputPixels.GetPixel(expectedX + width, expectedY + height - 1)[0]);
        Assert.Equal(expectedX, (2550 - width) / 2);
        Assert.Equal(expectedY, (2550 - height) / 2);
        CertificationArtifactStore.Capture("working-page", id, source, target);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
