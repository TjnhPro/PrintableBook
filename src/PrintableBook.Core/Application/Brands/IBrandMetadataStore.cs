using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Brands;

public interface IBrandMetadataStore
{
    ValueTask<BrandMetadata?> LoadAsync(
        DirectoryReference brandDirectory,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        DirectoryReference brandDirectory,
        BrandMetadata metadata,
        CancellationToken cancellationToken = default);
}
