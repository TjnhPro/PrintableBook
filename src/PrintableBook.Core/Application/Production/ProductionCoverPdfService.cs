using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Production;

public sealed record ProductionCoverPdfResult(
    FileReference CoverPdf,
    PhysicalPageSize PageSize,
    DateTimeOffset CompletedAtUtc,
    FileReference? PreviewPdf = null,
    CoverPanelPreviewOutcome? PanelPreviews = null);

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
        var temporaryDirectory = new DirectoryReference(Path.Combine(
            workspace.TemporaryOutputDirectory.Value,
            $"production-cover-{Guid.NewGuid():N}"));
        var sourceSnapshot = new FileReference(Path.Combine(temporaryDirectory.Value, "final_cover.png"));

        try
        {
            await fileSystem.CreateDirectoryAsync(temporaryDirectory, cancellationToken);
            await fileSystem.CopyFileAsync(source, sourceSnapshot, overwrite: true, cancellationToken);
            var actual = await imageInspector.GetSizeAsync(sourceSnapshot, cancellationToken);
            if (actual != expected)
            {
                throw new InvalidDataException($"Production final Cover is {actual.Width} x {actual.Height} px; required size is {expected.Width} x {expected.Height} px.");
            }

            var exported = await pdfExporter.ExportCoverAsync(
                new CoverPdfExportRequest(sourceSnapshot, temporaryDirectory, CoverPageSize),
                cancellationToken);
            var published = await outputPublisher.PublishCoverAsync(new CoverOutputPublicationRequest(
                workspace.BookId,
                exported,
                finalOutputRoot,
                ExpectedCoverPageCount: 1,
                CoverPageSize), cancellationToken);

            // Publication is the commit point. Finish state recording even if cancellation arrives afterward.
            var finalizationToken = CancellationToken.None;
            var completedAt = DateTimeOffset.UtcNow;
            var bookState = await bookStateStore.LoadAsync(workspace, finalizationToken) ?? BookProcessingState.NotStarted(workspace.BookId);
            await bookStateStore.SaveAsync(
                workspace,
                bookState.RecordPublishedArtifact(
                    PublishedArtifactKind.Cover,
                    published.CoverPdf.Value,
                    published.PreviewPdf?.Value),
                finalizationToken);
            var productionState = await productionStateStore.LoadAsync(workspace, finalizationToken);
            await productionStateStore.SaveAsync(
                workspace,
                productionState.RecordCoverOutput(
                    Path.GetFileName(published.CoverPdf.Value),
                    CreateInputSignature(ProductionFileSignature.From(metadata)),
                    completedAt,
                    published.PreviewPdf is null ? null : Path.GetFileName(published.PreviewPdf.Value)),
                finalizationToken);
            return new ProductionCoverPdfResult(
                published.CoverPdf,
                CoverPageSize,
                completedAt,
                published.PreviewPdf,
                published.PanelPreviews);
        }
        finally
        {
            await TryDeleteTemporaryDirectoryAsync(temporaryDirectory);
        }
    }

    private async ValueTask TryDeleteTemporaryDirectoryAsync(DirectoryReference temporaryDirectory)
    {
        try
        {
            await fileSystem.DeleteDirectoryAsync(temporaryDirectory, recursive: true, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preview cleanup is best effort and cannot invalidate a committed Cover PDF.
        }
    }

    public static string CreateInputSignature(ProductionFileSignature sourceSignature)
    {
        var canonical = $"production-cover-v1|{sourceSignature.LengthBytes}|{sourceSignature.LastWriteTimeUtc.UtcTicks}|{CoverPageSize.WidthInches:R}|{CoverPageSize.HeightInches:R}";
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }
}
