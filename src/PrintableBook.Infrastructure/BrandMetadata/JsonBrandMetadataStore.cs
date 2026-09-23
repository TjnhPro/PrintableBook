using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;

namespace PrintableBook.Infrastructure.Brands;

public sealed class JsonBrandMetadataStore(IFileSystem fileSystem) : IBrandMetadataStore
{
    private const string FileName = "brand.metadata.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async ValueTask<BrandMetadata?> LoadAsync(
        DirectoryReference brandDirectory,
        CancellationToken cancellationToken = default)
    {
        var file = MetadataFile(brandDirectory);
        if (!await fileSystem.FileExistsAsync(file, cancellationToken)) return null;

        var metadata = JsonSerializer.Deserialize<BrandMetadata>(
            await fileSystem.ReadTextAsync(file, cancellationToken),
            JsonOptions) ?? throw new JsonException("Brand metadata is empty.");
        return metadata.Normalize();
    }

    public ValueTask SaveAsync(
        DirectoryReference brandDirectory,
        BrandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return fileSystem.WriteTextAtomicallyAsync(
            MetadataFile(brandDirectory),
            JsonSerializer.Serialize(metadata.Normalize(), JsonOptions),
            cancellationToken);
    }

    private static FileReference MetadataFile(DirectoryReference brandDirectory) =>
        new(Path.Combine(brandDirectory.Value, FileName));
}
