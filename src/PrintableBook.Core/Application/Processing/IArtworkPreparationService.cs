namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Prepares classified source artwork without applying a brand frame.
/// </summary>
public interface IArtworkPreparationService
{
    ValueTask<PreparedArtwork> PrepareAsync(
        ArtworkPreparationRequest request,
        CancellationToken cancellationToken = default);
}
