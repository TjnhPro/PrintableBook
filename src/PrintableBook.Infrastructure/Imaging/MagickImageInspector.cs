using ImageMagick;
using PrintableBook.Core.Abstractions;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickImageInspector : IImageInspector
{
    public async ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default) =>
        (await GetInfoAsync(image, cancellationToken)).Size;

    public ValueTask<ImageInfo> GetInfoAsync(FileReference image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var magickImage = new MagickImage(image.Value);
        var density = magickImage.Density.ChangeUnits(DensityUnit.PixelsPerInch);
        return ValueTask.FromResult(new ImageInfo(
            new ImageSize((int)magickImage.Width, (int)magickImage.Height),
            new ImageDensity(density.X, density.Y)));
    }
}
