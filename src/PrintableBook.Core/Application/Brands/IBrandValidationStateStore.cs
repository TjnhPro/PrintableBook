using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Brands;

public interface IBrandValidationStateStore
{
    ValueTask<BrandValidationRecord?> LoadAsync(
        DirectoryReference brandDirectory,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        DirectoryReference brandDirectory,
        BrandValidationRecord record,
        CancellationToken cancellationToken = default);
}
