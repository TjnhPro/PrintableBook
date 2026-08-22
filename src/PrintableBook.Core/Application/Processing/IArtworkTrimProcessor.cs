namespace PrintableBook.Core.Application.Processing;

public interface IArtworkTrimProcessor
{
    ValueTask<ArtworkTrimResult> TrimAsync(ArtworkTrimRequest request, CancellationToken cancellationToken = default);
}
