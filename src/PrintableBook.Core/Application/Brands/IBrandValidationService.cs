using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.Brands;

public interface IBrandValidationService
{
    ValueTask<BrandValidationState> CheckStateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default);
    ValueTask<BrandValidationResult> ValidateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default);
}
