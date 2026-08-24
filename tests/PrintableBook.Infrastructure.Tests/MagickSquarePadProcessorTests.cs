using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickSquarePadProcessorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.SquarePadTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(10, 6, 0, 2)]
    [InlineData(11, 6, 0, 2)]
    [InlineData(6, 10, 2, 0)]
    [InlineData(6, 11, 2, 0)]
    [InlineData(6, 6, 0, 0)]
    [InlineData(7, 6, 0, 0)]
    [InlineData(6, 7, 0, 0)]
    public async Task PadAsync_centers_on_an_opaque_white_larger_side_square_using_floor_offsets(
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

        await new MagickSquarePadProcessor().PadAsync(new SquarePadRequest(
            new FileReference(source),
            new FileReference(target)));

        var side = Math.Max(width, height);
        using var output = new MagickImage(target);
        Assert.Equal((uint)side, output.Width);
        Assert.Equal((uint)side, output.Height);
        AssertCoordinate(output, expectedOriginX, expectedOriginY, 0, 0);
        AssertCoordinate(output, expectedOriginX + width - 1, expectedOriginY + height - 1, width - 1, height - 1);
        if (expectedOriginX != 0 || expectedOriginY != 0)
        {
            AssertWhite(output, 0, 0);
        }
        AssertOpaque(output);
    }

    [Fact]
    public async Task PadAsync_composites_transparent_source_pixels_onto_white()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "transparent.png");
        var target = Path.Combine(rootPath, "transparent.square.png");
        using (var image = new MagickImage(MagickColors.Transparent, 4, 2))
        {
            image.GetPixels().SetPixel(1, 0, [0, 0, 0, 255]);
            image.Write(source);
        }

        await new MagickSquarePadProcessor().PadAsync(new SquarePadRequest(
            new FileReference(source),
            new FileReference(target)));

        using var output = new MagickImage(target);
        var rgba = output.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException("Expected RGBA pixel data.");
        Assert.Equal([255, 255, 255, 255], rgba.Take(4));
        AssertOpaque(output);
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

    private static void AssertWhite(MagickImage image, int x, int y)
    {
        var pixel = image.GetPixels().GetPixel(x, y);
        Assert.Equal((byte)255, pixel[0]);
        Assert.Equal((byte)255, pixel[1]);
        Assert.Equal((byte)255, pixel[2]);
    }

    private static void AssertOpaque(MagickImage image)
    {
        var rgba = image.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidOperationException("Expected RGBA pixel data.");
        Assert.All(rgba.Where((_, index) => index % 4 == 3), alpha => Assert.Equal((byte)255, alpha));
    }
}
