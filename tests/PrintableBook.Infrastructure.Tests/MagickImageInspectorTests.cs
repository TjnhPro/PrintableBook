using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickImageInspectorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ImageTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task GetInfoAsync_reopens_a_real_png_with_dimensions_and_density()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "input.png");
        using (var image = new MagickImage(MagickColors.White, 64, 32))
        {
            image.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
            image.Write(source);
        }

        var info = await new MagickImageInspector().GetInfoAsync(new FileReference(source));

        Assert.Equal(new ImageSize(64, 32), info.Size);
        Assert.Equal(300, info.Density.Horizontal, precision: 2);
        Assert.Equal(300, info.Density.Vertical, precision: 2);
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
}
