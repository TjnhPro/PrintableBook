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
        var service = new ProductionCoverPdfService(
            fileSystem,
            new MagickImageInspector(),
            new PdfSharpPrintableBookPdfExporter(),
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
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
