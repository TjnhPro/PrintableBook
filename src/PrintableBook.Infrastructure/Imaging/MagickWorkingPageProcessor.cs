using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickWorkingPageProcessor : IWorkingPageProcessor
{
    public ValueTask CenterAsync(WorkingPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var artwork = new MagickImage(request.Source.Value);
        if (artwork.Width > request.PageSize.Width || artwork.Height > request.PageSize.Height)
        {
            throw new ArgumentException("Artwork must fit within the working page.", nameof(request));
        }

        using var page = new MagickImage(MagickColors.White, (uint)request.PageSize.Width, (uint)request.PageSize.Height);
        if (artwork.Density.X > 0 && artwork.Density.Y > 0) page.Density = artwork.Density;
        var x = (request.PageSize.Width - (int)artwork.Width) / 2;
        var y = (request.PageSize.Height - (int)artwork.Height) / 2;
        page.Composite(artwork, x, y, CompositeOperator.Over);
        var directory = Path.GetDirectoryName(request.Target.Value) ?? throw new ArgumentException("The working-page target must include a directory.", nameof(request));
        Directory.CreateDirectory(directory);
        page.Write(request.Target.Value);
        return ValueTask.CompletedTask;
    }
}
