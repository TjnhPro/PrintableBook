using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>Flattens an interior source onto white and writes one exact, nearest-neighbour canonical raster.</summary>
public sealed class MagickArtworkSourceNormalizer : IArtworkSourceNormalizer
{
    public ValueTask NormalizeAsync(ArtworkSourceNormalizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TargetSize.Width <= 0 || request.TargetSize.Height <= 0 || request.TargetSize.Width != request.TargetSize.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The normalized artwork target must be a positive square.");
        }

        using var source = new MagickImage(request.Source.Value);
        using var canvas = new MagickImage(MagickColors.White, (uint)request.TargetSize.Width, (uint)request.TargetSize.Height);
        source.FilterType = FilterType.Point;
        source.Resize((uint)request.TargetSize.Width, (uint)request.TargetSize.Height);
        canvas.Composite(source, Gravity.Center, CompositeOperator.Over);
        canvas.Alpha(AlphaOption.Remove);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(request.Destination.Value)
            ?? throw new ArgumentException("The normalized source destination must have a directory.", nameof(request));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(request.Destination.Value)}.{Guid.NewGuid():N}.tmp.png");
        try
        {
            canvas.Write(temporary, MagickFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, request.Destination.Value, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return ValueTask.CompletedTask;
    }
}
