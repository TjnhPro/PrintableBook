using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Production;

namespace PrintableBook.Infrastructure.Production;

public sealed class ProductionAssetImportService(
    IFileSystem fileSystem,
    IImageInspector imageInspector,
    IProductionWorkspaceStateStore stateStore) : IProductionAssetImportService
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public async ValueTask<ProductionAssetImportResult> ImportAsync(
        BookWorkspace workspace,
        ProductionAssetKind assetKind,
        FileReference selectedSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(selectedSource);
        var definition = ProductionAssets.Get(assetKind);
        if (!string.Equals(Path.GetExtension(selectedSource.Value), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProductionAssetImportException("production_asset_not_png", "Choose a readable PNG file.");
        }

        if (!await fileSystem.FileExistsAsync(selectedSource, cancellationToken))
        {
            throw new ProductionAssetImportException("production_asset_unreadable", "The selected PNG could not be read.");
        }

        await EnsurePngSignatureAsync(selectedSource, cancellationToken);
        var size = await InspectAsync(selectedSource, cancellationToken);
        ValidateSize(definition, size);

        var destination = ProductionWorkspacePaths.SourceFile(workspace, assetKind);
        var pending = new FileReference($"{destination.Value}.{Guid.NewGuid():N}.pending");
        try
        {
            await fileSystem.CopyFileAsync(selectedSource, pending, overwrite: true, cancellationToken);
            await EnsurePngSignatureAsync(pending, cancellationToken);
            var copiedSize = await InspectAsync(pending, cancellationToken);
            ValidateSize(definition, copiedSize);
            await fileSystem.MoveFileAsync(pending, destination, overwrite: true, cancellationToken);

            var metadata = await fileSystem.GetFileMetadataAsync(destination, cancellationToken)
                ?? throw new IOException("The imported Production asset metadata is unavailable.");
            var signature = ProductionFileSignature.From(metadata);
            var state = await stateStore.LoadAsync(workspace, cancellationToken);
            await stateStore.SaveAsync(
                workspace,
                state.RecordImportedAsset(assetKind, signature, DateTimeOffset.UtcNow),
                cancellationToken);
            return new ProductionAssetImportResult(assetKind, destination, copiedSize, signature);
        }
        catch (ProductionAssetImportException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductionAssetImportException(
                "production_asset_import_failed",
                "The Production asset could not be imported. The previous asset was kept when available.",
                exception);
        }
        finally
        {
            await fileSystem.DeleteFileAsync(pending, CancellationToken.None);
        }
    }

    private static async ValueTask EnsurePngSignatureAsync(FileReference source, CancellationToken cancellationToken)
    {
        try
        {
            var signature = new byte[PngSignature.Length];
            await using var stream = new FileStream(source.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var read = await stream.ReadAsync(signature, cancellationToken);
            if (read != PngSignature.Length || !signature.SequenceEqual(PngSignature))
            {
                throw new ProductionAssetImportException("production_asset_not_png", "Choose a readable PNG file.");
            }
        }
        catch (ProductionAssetImportException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductionAssetImportException("production_asset_unreadable", "The selected PNG could not be read.", exception);
        }
    }

    private async ValueTask<ImageSize> InspectAsync(FileReference source, CancellationToken cancellationToken)
    {
        try
        {
            return await imageInspector.GetSizeAsync(source, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ProductionAssetImportException("production_asset_unreadable", "The selected PNG could not be read.", exception);
        }
    }

    private static void ValidateSize(ProductionAssetDefinition definition, ImageSize actual)
    {
        if (definition.RequiredSize is not { } required || actual == required)
        {
            return;
        }

        throw new ProductionAssetImportException(
            "production_cover_size_invalid",
            $"Image is {actual.Width} x {actual.Height} px. Required size: {required.Width} x {required.Height} px.");
    }
}
