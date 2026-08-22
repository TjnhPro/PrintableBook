using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickFinalInteriorPageProcessor : IFinalInteriorPageProcessor
{
    public ValueTask ProduceAsync(FinalInteriorPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var image = new MagickImage(request.Source.Value);
        if (image.Width != request.ExpectedSize.Width || image.Height != request.ExpectedSize.Height)
        {
            throw new InvalidOperationException("The cached page does not match the configured final raster dimensions.");
        }

        image.Density = new Density(
            request.TargetDensity.Horizontal,
            request.TargetDensity.Vertical,
            DensityUnit.PixelsPerInch);
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The final-page target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        image.Write(request.Target.Value);
        return ValueTask.CompletedTask;
    }
}
