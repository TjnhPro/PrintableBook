namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Determines an artwork type by composing certified detector evidence.
/// </summary>
public interface IArtworkClassifier
{
    ValueTask<ArtworkClassificationResult> ClassifyAsync(
        ArtworkClassificationRequest request,
        CancellationToken cancellationToken = default);
}
