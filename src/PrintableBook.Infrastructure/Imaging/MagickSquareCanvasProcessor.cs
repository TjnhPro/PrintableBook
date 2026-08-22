using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickSquareCanvasProcessor : ISquareCanvasProcessor
{
    public ValueTask NormalizeAsync(SquareCanvasRequest request, CancellationToken cancellationToken = default)
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

        canvas.Composite(source, Gravity.Center, CompositeOperator.Over);
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The canvas target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        canvas.Write(request.Target.Value);

        return ValueTask.CompletedTask;
    }
}
