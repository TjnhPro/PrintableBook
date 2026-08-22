using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Infrastructure.Discovery;

public sealed class JsonBrandSettingsStore(IFileSystem fileSystem) : IBrandSettingsStore
{
    public async ValueTask<string> LoadAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken = default)
    {
        var file = new FileReference(Path.Combine(brandDirectory.Value, "brand.json"));
        return await fileSystem.FileExistsAsync(file, cancellationToken)
            ? await fileSystem.ReadTextAsync(file, cancellationToken)
            : "{}";
    }

    public async ValueTask SaveAsync(DirectoryReference brandDirectory, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Brand settings must be a JSON object.", nameof(json));
        }

        await fileSystem.WriteTextAtomicallyAsync(
            new FileReference(Path.Combine(brandDirectory.Value, "brand.json")),
            JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }
}
