using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Desktop;

/// <summary>Stores extensible Brand-only JSON without mixing it into global application settings.</summary>
public interface IBrandSettingsStore
{
    ValueTask<string> LoadAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(DirectoryReference brandDirectory, string json, CancellationToken cancellationToken = default);
}
