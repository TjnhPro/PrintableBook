using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record ArtworkSourceNormalizationRequest(FileReference Source, FileReference Destination, ImageSize TargetSize);

/// <summary>Creates the one canonical, opaque raster used by all following interior stages.</summary>
public interface IArtworkSourceNormalizer
{
    ValueTask NormalizeAsync(ArtworkSourceNormalizationRequest request, CancellationToken cancellationToken = default);
}
