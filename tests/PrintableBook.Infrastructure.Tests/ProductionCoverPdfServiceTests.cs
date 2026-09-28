using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Pdf;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class ProductionCoverPdfServiceTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ProductionCover.{Guid.NewGuid():N}");

    [Fact]
    public async Task BuildAsync_publishes_the_rounded_cover_and_preserves_interior_artifact_state()
    {
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(
            new BookId("Book One"),
            new DirectoryReference(Path.Combine(rootPath, "Book One")));
        var source = ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.FinalCover);
        using (var image = new MagickImage(MagickColors.White, 5242, 2626))
        {
            image.Format = MagickFormat.Png24;
            image.Write(source.Value);
        }

        var bookStateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var previousInterior = Path.Combine(rootPath, "Book One", "Output", "Book One - Interior.pdf");
        await bookStateStore.SaveAsync(
            workspace,
            BookProcessingState.NotStarted(workspace.BookId)
                .RecordPublishedArtifact(PublishedArtifactKind.Interior, previousInterior));
        var productionStateStore = new JsonProductionWorkspaceStateStore(fileSystem);
        var exporter = new CapturingExporter(new PdfSharpPrintableBookPdfExporter());
        var service = new ProductionCoverPdfService(
            fileSystem,
            new MagickImageInspector(),
            exporter,
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()),
            bookStateStore,
            productionStateStore);

        var result = await service.BuildAsync(
            workspace,
            new DirectoryReference(Path.Combine(rootPath, "Book One", "Output")));

        Assert.Equal(new PhysicalPageSize(17.47, 8.75), result.PageSize);
        Assert.True(File.Exists(result.CoverPdf.Value));
        var state = await bookStateStore.LoadAsync(workspace);
        Assert.Contains(previousInterior, state!.PublishedArtifactReferences!);
        Assert.Contains(result.CoverPdf.Value, state.PublishedArtifactReferences!);
        var productionState = await productionStateStore.LoadAsync(workspace);
        Assert.Equal(Path.GetFileName(result.CoverPdf.Value), productionState.CoverOutput!.FileName);
        Assert.StartsWith("sha256:", productionState.CoverOutput.InputSignature, StringComparison.Ordinal);
        Assert.NotNull(exporter.CoverSource);
        Assert.NotEqual(source.Value, exporter.CoverSource!.Value);
        Assert.Contains("production-cover-", exporter.CoverSource.Value, StringComparison.Ordinal);
        Assert.False(File.Exists(exporter.CoverSource.Value));
        Assert.Equal(CoverPanelPreviewStatus.Ready, result.PanelPreviews?.Status);
        Assert.True(File.Exists(result.PanelPreviews!.Pair!.BackCover.Value));
        Assert.True(File.Exists(result.PanelPreviews.Pair.FrontCover.Value));
    }

    [Fact]
    public async Task BuildAsync_completes_state_recording_when_cancellation_arrives_after_publication()
    {
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(
            new BookId("Book Two"),
            new DirectoryReference(Path.Combine(rootPath, "Book Two")));
        var source = ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.FinalCover);
        using (var image = new MagickImage(MagickColors.White, 5242, 2626))
        {
            image.Format = MagickFormat.Png24;
            image.Write(source.Value);
        }

        using var cancellation = new CancellationTokenSource();
        var bookStateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var productionStateStore = new JsonProductionWorkspaceStateStore(fileSystem);
        var publisher = new CancelAfterPublishPublisher(
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()),
            cancellation);
        var service = new ProductionCoverPdfService(
            fileSystem,
            new MagickImageInspector(),
            new PdfSharpPrintableBookPdfExporter(),
            publisher,
            bookStateStore,
            productionStateStore);

        var result = await service.BuildAsync(
            workspace,
            new DirectoryReference(Path.Combine(rootPath, "Book Two", "Output")),
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(File.Exists(result.CoverPdf.Value));
        Assert.NotNull((await bookStateStore.LoadAsync(workspace))?.PublishedArtifactReferences);
        Assert.NotNull((await productionStateStore.LoadAsync(workspace)).CoverOutput);
    }

    private sealed class CapturingExporter(IPrintableBookPdfExporter inner) : IPrintableBookPdfExporter
    {
        public FileReference? CoverSource { get; private set; }

        public ValueTask<PrintableBookPdfExportResult> ExportAsync(PrintableBookPdfExportRequest request, CancellationToken cancellationToken = default) =>
            inner.ExportAsync(request, cancellationToken);

        public ValueTask<InteriorPdfExportResult> ExportInteriorAsync(InteriorPdfExportRequest request, CancellationToken cancellationToken = default) =>
            inner.ExportInteriorAsync(request, cancellationToken);

        public ValueTask<CoverPdfExportResult> ExportCoverAsync(CoverPdfExportRequest request, CancellationToken cancellationToken = default)
        {
            CoverSource = request.Cover;
            return inner.ExportCoverAsync(request, cancellationToken);
        }
    }

    private sealed class CancelAfterPublishPublisher(
        IBookOutputPublisher inner,
        CancellationTokenSource cancellation) : IBookOutputPublisher
    {
        public ValueTask<PublishedBookOutputs> PublishAsync(BookOutputPublicationRequest request, CancellationToken cancellationToken = default) =>
            inner.PublishAsync(request, cancellationToken);

        public ValueTask<PublishedInteriorOutput> PublishInteriorAsync(InteriorOutputPublicationRequest request, CancellationToken cancellationToken = default) =>
            inner.PublishInteriorAsync(request, cancellationToken);

        public async ValueTask<PublishedCoverOutput> PublishCoverAsync(CoverOutputPublicationRequest request, CancellationToken cancellationToken = default)
        {
            var published = await inner.PublishCoverAsync(request, cancellationToken);
            await cancellation.CancelAsync();
            return published;
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
