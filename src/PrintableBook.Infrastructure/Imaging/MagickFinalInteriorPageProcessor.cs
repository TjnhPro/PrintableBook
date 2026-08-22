using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickFinalInteriorPageProcessor : IFinalInteriorPageProcessor
{
    public ValueTask ProduceAsync(FinalInteriorPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var workingPage = new MagickImage(request.Source.Value);
        if (workingPage.Width > request.ExpectedSize.Width || workingPage.Height > request.ExpectedSize.Height)
        {
            throw new InvalidOperationException("The cached working page does not fit the configured final raster dimensions.");
        }

        using var finalPage = new MagickImage(MagickColors.White, (uint)request.ExpectedSize.Width, (uint)request.ExpectedSize.Height);
        finalPage.Density = new Density(
            request.TargetDensity.Horizontal,
            request.TargetDensity.Vertical,
            DensityUnit.PixelsPerInch);
        var x = (request.ExpectedSize.Width - (int)workingPage.Width) / 2;
        var y = (request.ExpectedSize.Height - (int)workingPage.Height) / 2;
        finalPage.Composite(workingPage, x, y, CompositeOperator.Over);
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The final-page target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        finalPage.Write(request.Target.Value);
        return ValueTask.CompletedTask;
    }
}
