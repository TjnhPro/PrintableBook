using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Processing;

/// <summary>
/// Applies the approved CropArt path: trim, larger-side white padding, then square resize.
/// </summary>
public sealed class CropArtPreparationProcessor(
    IArtworkTrimProcessor trimProcessor,
    ISquarePadProcessor squarePadProcessor,
    IArtworkResizeProcessor resizeProcessor)
{
    public async ValueTask PrepareAsync(
        ArtworkPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Classification.Type != ArtworkType.CropArt)
        {
            throw new ArgumentException("CropArt preparation requires CropArt classification.", nameof(request));
        }

        if (request.PreparedArtworkSize.Width != request.PreparedArtworkSize.Height)
        {
            throw new ArgumentException("Prepared artwork size must be square.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The prepared target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        var trimmed = TemporaryFile(targetDirectory, request.Target, "trimmed");
        var square = TemporaryFile(targetDirectory, request.Target, "square");

        try
        {
            var trimResult = await trimProcessor.TrimAsync(
                new ArtworkTrimRequest(request.Source, trimmed, request.ArtworkDetectionThreshold),
                cancellationToken);
            if (!trimResult.HasArtwork)
            {
                throw new InvalidDataException("CropArt preparation found no artwork after trim.");
            }

            await squarePadProcessor.PadAsync(new SquarePadRequest(trimmed, square), cancellationToken);
            await resizeProcessor.ResizeAsync(
                new ArtworkResizeRequest(square, request.Target, request.PreparedArtworkSize.Width, request.TargetDensity),
                cancellationToken);
        }
        finally
        {
            DeleteIfPresent(trimmed);
            DeleteIfPresent(square);
        }
    }

    private static FileReference TemporaryFile(string directory, FileReference target, string stage) =>
        new(Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(target.Value)}.{stage}.{Guid.NewGuid():N}.png"));

    private static void DeleteIfPresent(FileReference file)
    {
        if (File.Exists(file.Value))
        {
            File.Delete(file.Value);
        }
    }
}
