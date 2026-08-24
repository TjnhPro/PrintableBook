using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Center-crops the longer source axis, leaving an odd extra pixel on the right or bottom.
/// </summary>
public sealed class MagickSquareCropProcessor : ISquareCropProcessor
{
    public ValueTask CropAsync(SquareCropRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(request.Source.Value);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var side = Math.Min(width, height);
        var x = (width - side) / 2;
        var y = (height - side) / 2;
        image.Crop(new MagickGeometry(x, y, (uint)side, (uint)side));
        image.ResetPage();
        cancellationToken.ThrowIfCancellationRequested();

        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The square crop target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        image.Write(request.Target.Value);

        return ValueTask.CompletedTask;
    }
}
