using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Production;

public sealed record ProductionAssetImportResult(
    ProductionAssetKind AssetKind,
    FileReference Destination,
    ImageSize Size,
    ProductionFileSignature Signature);

public sealed class ProductionAssetImportException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code)
        ? throw new ArgumentException("An import error code is required.", nameof(code))
        : code;
}

public interface IProductionAssetImportService
{
    ValueTask<ProductionAssetImportResult> ImportAsync(
        BookWorkspace workspace,
        ProductionAssetKind assetKind,
        FileReference selectedSource,
        CancellationToken cancellationToken = default);
}
