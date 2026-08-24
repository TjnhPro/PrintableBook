using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Crops source artwork to the pixels strictly inside inclusive BorderLine bounds.
/// </summary>
public sealed class MagickBorderBoundsCropProcessor : IBorderBoundsCropProcessor
{
    public ValueTask CropAsync(BorderBoundsCropRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.BorderBounds.Size.Width <= 2 || request.BorderBounds.Size.Height <= 2)
        {
            throw new ArgumentException("Border bounds must enclose at least one strictly inside pixel.", nameof(request));
        }

        using var image = new MagickImage(request.Source.Value);
        var crop = StrictInside(request, checked((int)image.Width), checked((int)image.Height));
        image.Crop(new MagickGeometry(crop.X, crop.Y, (uint)crop.Width, (uint)crop.Height));
        image.ResetPage();
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The crop target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        image.Write(request.Target.Value);

        return ValueTask.CompletedTask;
    }

    private static CropGeometry StrictInside(BorderBoundsCropRequest request, int imageWidth, int imageHeight)
    {
        var bounds = request.BorderBounds;
        var rightExclusive = checked(bounds.Origin.X + bounds.Size.Width);
        var bottomExclusive = checked(bounds.Origin.Y + bounds.Size.Height);
        if (bounds.Origin.X < 0 || bounds.Origin.Y < 0 || rightExclusive > imageWidth || bottomExclusive > imageHeight)
        {
            throw new ArgumentException("Border bounds must lie inside the source raster.", nameof(request));
        }

        return new CropGeometry(
            checked(bounds.Origin.X + 1),
            checked(bounds.Origin.Y + 1),
            checked(bounds.Size.Width - 2),
            checked(bounds.Size.Height - 2));
    }

    private readonly record struct CropGeometry(int X, int Y, int Width, int Height);
}
