using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickArtworkResizeProcessor : IArtworkResizeProcessor
{
    public ValueTask ResizeAsync(ArtworkResizeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TargetSize.Width != request.TargetSize.Height)
        {
            throw new ArgumentException("Normalized artwork requires a square resize target.", nameof(request));
        }

        using var image = new MagickImage(request.Source.Value);
        if (image.Width != image.Height)
        {
            throw new ArgumentException("Artwork must be square before resizing.", nameof(request));
        }

        image.FilterType = FilterType.Lanczos;
        image.Resize((uint)request.TargetSize.Width, (uint)request.TargetSize.Height);
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
