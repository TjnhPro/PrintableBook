using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Production;

public sealed record ProductionCoverPdfResult(
    FileReference CoverPdf,
    PhysicalPageSize PageSize,
    DateTimeOffset CompletedAtUtc);

public interface IProductionCoverPdfService
{
    ValueTask<ProductionCoverPdfResult> BuildAsync(
        BookWorkspace workspace,
        DirectoryReference finalOutputRoot,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionCoverPdfService(
    IFileSystem fileSystem,
    IImageInspector imageInspector,
    IPrintableBookPdfExporter pdfExporter,
    IBookOutputPublisher outputPublisher,
    IBookWorkspaceStateStore bookStateStore,
    IProductionWorkspaceStateStore productionStateStore) : IProductionCoverPdfService
{
    public static PhysicalPageSize CoverPageSize { get; } = new(17.47, 8.75);

    public async ValueTask<ProductionCoverPdfResult> BuildAsync(
        BookWorkspace workspace,
        DirectoryReference finalOutputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(finalOutputRoot);
        var source = ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.FinalCover);
        var metadata = await fileSystem.GetFileMetadataAsync(source, cancellationToken)
            ?? throw new FileNotFoundException("The Production final Cover is missing.", source.Value);
        var expected = ProductionAssets.Get(ProductionAssetKind.FinalCover).RequiredSize!.Value;
        var actual = await imageInspector.GetSizeAsync(source, cancellationToken);
        if (actual != expected)
        {
            throw new InvalidDataException($"Production final Cover is {actual.Width} x {actual.Height} px; required size is {expected.Width} x {expected.Height} px.");
        }

        var temporaryDirectory = new DirectoryReference(Path.Combine(workspace.TemporaryOutputDirectory.Value, "production-cover"));
        await fileSystem.DeleteDirectoryAsync(temporaryDirectory, recursive: true, cancellationToken);
        var exported = await pdfExporter.ExportCoverAsync(new CoverPdfExportRequest(source, temporaryDirectory, CoverPageSize), cancellationToken);
        var published = await outputPublisher.PublishCoverAsync(new CoverOutputPublicationRequest(
            workspace.BookId,
            exported,
            finalOutputRoot,
            ExpectedCoverPageCount: 1,
            CoverPageSize), cancellationToken);

        var completedAt = DateTimeOffset.UtcNow;
        var bookState = await bookStateStore.LoadAsync(workspace, cancellationToken) ?? BookProcessingState.NotStarted(workspace.BookId);
        await bookStateStore.SaveAsync(
            workspace,
            bookState.RecordPublishedArtifact(PublishedArtifactKind.Cover, published.CoverPdf.Value),
            cancellationToken);
        var productionState = await productionStateStore.LoadAsync(workspace, cancellationToken);
        await productionStateStore.SaveAsync(
            workspace,
            productionState.RecordCoverOutput(
                Path.GetFileName(published.CoverPdf.Value),
                CreateInputSignature(ProductionFileSignature.From(metadata)),
                completedAt),
            cancellationToken);
        return new ProductionCoverPdfResult(published.CoverPdf, CoverPageSize, completedAt);
    }

    public static string CreateInputSignature(ProductionFileSignature sourceSignature)
    {
        var canonical = $"production-cover-v1|{sourceSignature.LengthBytes}|{sourceSignature.LastWriteTimeUtc.UtcTicks}|{CoverPageSize.WidthInches:R}|{CoverPageSize.HeightInches:R}";
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }
}
