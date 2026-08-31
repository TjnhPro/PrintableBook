using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;

namespace PrintableBook.Infrastructure.BrandValidation;

public sealed class JsonBrandValidationStateStore(IFileSystem fileSystem) : IBrandValidationStateStore
{
    private const string StateFileName = "brand.validation.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async ValueTask<BrandValidationRecord?> LoadAsync(
        DirectoryReference brandDirectory,
        CancellationToken cancellationToken = default)
    {
        var file = GetStateFile(brandDirectory);
        if (!await fileSystem.FileExistsAsync(file, cancellationToken))
        {
            return null;
        }

        return JsonSerializer.Deserialize<BrandValidationRecord>(
            await fileSystem.ReadTextAsync(file, cancellationToken),
            JsonOptions)
            ?? throw new JsonException("Brand validation state is empty.");
    }

    public ValueTask SaveAsync(
        DirectoryReference brandDirectory,
        BrandValidationRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return fileSystem.WriteTextAtomicallyAsync(
            GetStateFile(brandDirectory),
            JsonSerializer.Serialize(record, JsonOptions),
            cancellationToken);
    }

    private static FileReference GetStateFile(DirectoryReference brandDirectory) =>
        new(Path.Combine(brandDirectory.Value, StateFileName));
}
