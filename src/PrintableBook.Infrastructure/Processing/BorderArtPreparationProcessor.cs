using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Processing;

/// <summary>
/// Applies the approved BorderArt path using existing BorderLine evidence without rerunning detection.
/// </summary>
public sealed class BorderArtPreparationProcessor(
    IBorderBoundsCropProcessor borderBoundsCropProcessor,
    ISquareCropProcessor squareCropProcessor,
    IArtworkResizeProcessor resizeProcessor)
{
    public async ValueTask PrepareAsync(
        ArtworkPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Classification.Type != ArtworkType.BorderArt || !request.Classification.BorderLine.HasBorder)
        {
            throw new ArgumentException("BorderArt preparation requires positive BorderLine evidence.", nameof(request));
        }

        if (request.Classification.BorderLine.BorderBounds is not { } borderBounds)
        {
            throw new ArgumentException("BorderArt preparation requires BorderLine bounds.", nameof(request));
        }

        if (request.PreparedArtworkSize.Width != request.PreparedArtworkSize.Height)
        {
            throw new ArgumentException("Prepared artwork size must be square.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var targetDirectory = Path.GetDirectoryName(request.Target.Value)
            ?? throw new ArgumentException("The prepared target must include a directory.", nameof(request));
        Directory.CreateDirectory(targetDirectory);
        var inside = TemporaryFile(targetDirectory, request.Target, "inside");
        var square = TemporaryFile(targetDirectory, request.Target, "square");

        try
        {
            await borderBoundsCropProcessor.CropAsync(
                new BorderBoundsCropRequest(request.Source, inside, borderBounds),
                cancellationToken);
            await squareCropProcessor.CropAsync(new SquareCropRequest(inside, square), cancellationToken);
            await resizeProcessor.ResizeAsync(
                new ArtworkResizeRequest(square, request.Target, request.PreparedArtworkSize.Width, request.TargetDensity),
                cancellationToken);
        }
        finally
        {
            DeleteIfPresent(inside);
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
