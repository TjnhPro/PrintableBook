using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickSquareCropProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.SquareCropTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(10, 6, 2, 0)]
    [InlineData(11, 6, 2, 0)]
    [InlineData(6, 10, 0, 2)]
    [InlineData(6, 11, 0, 2)]
    [InlineData(6, 6, 0, 0)]
    [InlineData(7, 6, 0, 0)]
    [InlineData(6, 7, 0, 0)]
    public async Task CropAsync_center_crops_to_the_smaller_side_using_floor_offsets(
        int width,
        int height,
        int expectedOriginX,
        int expectedOriginY)
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, $"{width}x{height}.png");
        var target = Path.Combine(rootPath, $"{width}x{height}.square.png");
        using (var image = CreateCoordinateImage(width, height))
        {
            image.Write(source);
        }

        await new MagickSquareCropProcessor().CropAsync(new SquareCropRequest(
            new FileReference(source),
            new FileReference(target)));

        var side = Math.Min(width, height);
        using var output = new MagickImage(target);
        Assert.Equal((uint)side, output.Width);
        Assert.Equal((uint)side, output.Height);
        AssertCoordinate(output, 0, 0, expectedOriginX, expectedOriginY);
        AssertCoordinate(output, side - 1, side - 1, expectedOriginX + side - 1, expectedOriginY + side - 1);
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

    private static MagickImage CreateCoordinateImage(int width, int height)
    {
        var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.GetPixels().SetPixel(x, y, [(byte)x, (byte)y, 100]);
            }
        }

        return image;
    }

    private static void AssertCoordinate(MagickImage image, int x, int y, int sourceX, int sourceY)
    {
        var pixel = image.GetPixels().GetPixel(x, y);
        Assert.Equal((byte)sourceX, pixel[0]);
        Assert.Equal((byte)sourceY, pixel[1]);
        Assert.Equal((byte)100, pixel[2]);
    }
}
