using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickFrameProcessor : IFrameProcessor
{
    public ValueTask ApplyAsync(FrameOverlayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The frame target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);

        if (!request.Enabled)
        {
            File.Copy(request.Source.Value, request.Target.Value, overwrite: true);
            return ValueTask.CompletedTask;
        }

        if (request.Frame is null || !File.Exists(request.Frame.Value))
        {
            throw new FileNotFoundException("An enabled frame requires a readable frame file.", request.Frame?.Value);
        }

        using var page = new MagickImage(request.Source.Value);
        using var frame = new MagickImage(request.Frame.Value);
        if (page.Width != frame.Width || page.Height != frame.Height)
        {
            throw new ArgumentException("The frame raster must match the page raster exactly.", nameof(request));
        }

        page.Composite(frame, CompositeOperator.Over);
        page.Write(request.Target.Value);
        return ValueTask.CompletedTask;
    }
}
