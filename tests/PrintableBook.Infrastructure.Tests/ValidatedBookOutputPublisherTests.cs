using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Pdf;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class ValidatedBookOutputPublisherTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.PublishTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task PublishAsync_moves_a_complete_validated_pdf_set_as_one_versioned_output_directory()
    {
        Directory.CreateDirectory(rootPath);
        var image = await CreatePngAsync();
        var temporaryOutput = new DirectoryReference(Path.Combine(rootPath, "workspace", "output"));
        var exported = await new MagickPrintableBookPdfExporter().ExportAsync(new PrintableBookPdfExportRequest(
            image, [image], temporaryOutput, new PhysicalPageSize(2, 1), new PhysicalPageSize(8.5, 8.5)));
        var publisher = new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector());

        var published = await publisher.PublishAsync(new BookOutputPublicationRequest(
            exported,
            new DirectoryReference(Path.Combine(rootPath, "final")),
            new PrintableBookPdfValidation(1, 1, new PhysicalPageSize(2, 1), new PhysicalPageSize(8.5, 8.5))));

        Assert.True(File.Exists(published.CoverPdf.Value));
        Assert.True(File.Exists(published.InteriorPdf.Value));
        Assert.False(Directory.Exists(temporaryOutput.Value));
        Assert.StartsWith(Path.Combine(rootPath, "final"), published.PublishedDirectory.Value, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<FileReference> CreatePngAsync()
    {
        var path = Path.Combine(rootPath, "page.png");
        using (var image = new MagickImage(MagickColors.White, 2550, 2550))
        {
            image.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
            image.Write(path);
        }

        await Task.CompletedTask;
        return new FileReference(path);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }
}
