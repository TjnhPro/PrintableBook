namespace PrintableBook.Core.Domain.Brands;

/// <summary>
/// Implemented by Infrastructure to resolve a profile from its selected source.
/// </summary>
public interface IBrandProfileResolver
{
    ValueTask<BrandProfile> ResolveAsync(
        BrandProfileReference reference,
        CancellationToken cancellationToken = default);
}
