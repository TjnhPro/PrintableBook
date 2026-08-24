using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Centers source pixels on an opaque white square, leaving odd padding on the right or bottom.
/// </summary>
public sealed class MagickSquarePadProcessor : ISquarePadProcessor
{
    public ValueTask PadAsync(SquarePadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var source = new MagickImage(request.Source.Value);
        var side = Math.Max(source.Width, source.Height);
        using var canvas = new MagickImage(MagickColors.White, side, side);
        if (source.Density.X > 0 && source.Density.Y > 0)
        {
            canvas.Density = source.Density;
        }

        var x = checked(((int)side - (int)source.Width) / 2);
        var y = checked(((int)side - (int)source.Height) / 2);
        canvas.Composite(source, x, y, CompositeOperator.Over);
        canvas.BackgroundColor = MagickColors.White;
        canvas.Alpha(AlphaOption.Off);
        cancellationToken.ThrowIfCancellationRequested();

        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The square padding target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        canvas.Write(request.Target.Value);

        return ValueTask.CompletedTask;
    }
}
