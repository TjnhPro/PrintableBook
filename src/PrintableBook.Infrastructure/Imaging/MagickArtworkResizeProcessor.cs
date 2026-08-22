using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickArtworkResizeProcessor : IArtworkResizeProcessor
{
    public ValueTask ResizeAsync(ArtworkResizeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MaximumSide <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The maximum artwork side must be positive.");
        }

        using var image = new MagickImage(request.Source.Value);
        image.FilterType = FilterType.Lanczos;
        var sourceMaximumSide = Math.Max(image.Width, image.Height);
        var scale = request.MaximumSide / (double)sourceMaximumSide;
        var targetWidth = (uint)Math.Round(image.Width * scale, MidpointRounding.AwayFromZero);
        var targetHeight = (uint)Math.Round(image.Height * scale, MidpointRounding.AwayFromZero);
        image.Resize(targetWidth, targetHeight);
        image.Density = new Density(
            request.TargetDensity.Horizontal,
            request.TargetDensity.Vertical,
            DensityUnit.PixelsPerInch);
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The resize target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        image.Write(request.Target.Value);

        return ValueTask.CompletedTask;
    }
}
