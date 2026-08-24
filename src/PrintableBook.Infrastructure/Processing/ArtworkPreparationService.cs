using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Processing;

/// <summary>
/// Selects the type-specific preparation path and validates the common prepared-artwork gate.
/// </summary>
public sealed class ArtworkPreparationService(
    BorderArtPreparationProcessor borderArtPreparationProcessor,
    FullArtPreparationProcessor fullArtPreparationProcessor,
    CropArtPreparationProcessor cropArtPreparationProcessor,
    IImageInspector imageInspector) : IArtworkPreparationService
{
    public async ValueTask<PreparedArtwork> PrepareAsync(
        ArtworkPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Classification);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(Path.GetExtension(request.Target.Value), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Prepared artwork must target a PNG file.", nameof(request));
        }

        var autoFrameRecommended = request.Classification.Type switch
        {
            ArtworkType.BorderArt => await PrepareBorderArtAsync(request, cancellationToken),
            ArtworkType.FullArt => await PrepareFullArtAsync(request, cancellationToken),
            ArtworkType.CropArt => await PrepareCropArtAsync(request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Classification.Type,
                "The artwork type is not supported for preparation.")
        };

        var output = await imageInspector.GetInfoAsync(request.Target, cancellationToken);
        if (output.Size != request.PreparedArtworkSize)
        {
            throw new InvalidDataException(
                $"Prepared artwork must be {request.PreparedArtworkSize.Width}x{request.PreparedArtworkSize.Height}, " +
                $"but was {output.Size.Width}x{output.Size.Height}.");
        }

        return PreparedArtwork.FromCached(request.Target, request.Classification.Type) with { AutoFrameRecommended = autoFrameRecommended };
    }

    private async ValueTask<bool> PrepareBorderArtAsync(ArtworkPreparationRequest request, CancellationToken cancellationToken)
    {
        await borderArtPreparationProcessor.PrepareAsync(request, cancellationToken);
        return true;
    }

    private async ValueTask<bool> PrepareFullArtAsync(ArtworkPreparationRequest request, CancellationToken cancellationToken)
    {
        await fullArtPreparationProcessor.PrepareAsync(request, cancellationToken);
        return true;
    }

    private async ValueTask<bool> PrepareCropArtAsync(ArtworkPreparationRequest request, CancellationToken cancellationToken)
    {
        await cropArtPreparationProcessor.PrepareAsync(request, cancellationToken);
        return false;
    }
}
