using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Production;

public readonly record struct ProductionFileSignature(long LengthBytes, DateTimeOffset LastWriteTimeUtc)
{
    public static ProductionFileSignature From(FileMetadata metadata) =>
        new(metadata.LengthBytes, metadata.LastWriteTimeUtc);
}

public sealed record ProductionAssetState(
    string FileName,
    ProductionFileSignature Signature,
    DateTimeOffset ImportedAtUtc);

public sealed record ProductionProcessedPageState(
    string FileName,
    ProductionFileSignature SourceSignature,
    string ProcessingSettingsSignature,
    DateTimeOffset CompletedAtUtc);

public sealed record ProductionOutputState(
    string FileName,
    string InputSignature,
    DateTimeOffset CompletedAtUtc);

public sealed record ProductionWorkspaceState(
    int SchemaVersion,
    IReadOnlyDictionary<string, ProductionAssetState>? Assets = null,
    IReadOnlyDictionary<string, ProductionProcessedPageState>? ProcessedPages = null,
    ProductionOutputState? CoverOutput = null,
    ProductionOutputState? InteriorOutput = null)
{
    public const int CurrentSchemaVersion = 1;

    public static ProductionWorkspaceState Empty { get; } = new(CurrentSchemaVersion);

    public ProductionAssetState? GetAsset(ProductionAssetKind kind) =>
        Assets is not null && Assets.TryGetValue(ProductionAssets.Get(kind).FileName, out var value) ? value : null;

    public ProductionWorkspaceState RecordImportedAsset(
        ProductionAssetKind kind,
        ProductionFileSignature signature,
        DateTimeOffset importedAtUtc)
    {
        var definition = ProductionAssets.Get(kind);
        var assets = Assets is null
            ? new Dictionary<string, ProductionAssetState>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProductionAssetState>(Assets, StringComparer.OrdinalIgnoreCase);
        assets[definition.FileName] = new ProductionAssetState(definition.FileName, signature, importedAtUtc);
        return this with { SchemaVersion = CurrentSchemaVersion, Assets = assets };
    }
}

public interface IProductionWorkspaceStateStore
{
    ValueTask<ProductionWorkspaceState> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(BookWorkspace workspace, ProductionWorkspaceState state, CancellationToken cancellationToken = default);
}
