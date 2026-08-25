using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.Pdf;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class ValidatedBookOutputPublisherTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.PublishTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task PublishAsync_writes_the_latest_full_output_directly_into_the_book_output_directory()
    {
        Directory.CreateDirectory(rootPath);
        var image = await CreatePngAsync();
        var temporaryOutput = new DirectoryReference(Path.Combine(rootPath, "Book One", ".workspace", "output-temp"));
        var exported = await ExportFullAsync(image, temporaryOutput);
        var output = new DirectoryReference(Path.Combine(rootPath, "Book One", "Output"));
        var publisher = new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector());

        var published = await publisher.PublishAsync(new BookOutputPublicationRequest(
            new BookId("Book One"),
            exported,
            output,
            new PrintableBookPdfValidation(1, 1, new PhysicalPageSize(2, 1), new PhysicalPageSize(8.5, 8.5))));

        Assert.Equal(Path.Combine(output.Value, "Book One - Cover.pdf"), published.CoverPdf.Value);
        Assert.Equal(Path.Combine(output.Value, "Book One - Interior.pdf"), published.InteriorPdf.Value);
        Assert.Equal(output, published.PublishedDirectory);
        Assert.True(File.Exists(published.CoverPdf.Value));
        Assert.True(File.Exists(published.InteriorPdf.Value));
        Assert.False(Directory.Exists(temporaryOutput.Value));
        Assert.DoesNotContain(Directory.EnumerateDirectories(output.Value), path => Path.GetFileName(path).StartsWith("run-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublishAsync_replaces_previous_latest_files_after_validation()
    {
        Directory.CreateDirectory(rootPath);
        var output = new DirectoryReference(Path.Combine(rootPath, "Book One", "Output"));
        Directory.CreateDirectory(output.Value);
        await File.WriteAllTextAsync(Path.Combine(output.Value, "Book One - Cover.pdf"), "old-cover");
        await File.WriteAllTextAsync(Path.Combine(output.Value, "Book One - Interior.pdf"), "old-interior");
        var exported = await ExportFullAsync(await CreatePngAsync(), new DirectoryReference(Path.Combine(rootPath, "Book One", ".workspace", "output-temp")));

        var published = await new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()).PublishAsync(
            new BookOutputPublicationRequest(new BookId("Book One"), exported, output,
                new PrintableBookPdfValidation(1, 1, new PhysicalPageSize(2, 1), new PhysicalPageSize(8.5, 8.5))));

        Assert.StartsWith("%PDF", await File.ReadAllTextAsync(published.CoverPdf.Value), StringComparison.Ordinal);
        Assert.StartsWith("%PDF", await File.ReadAllTextAsync(published.InteriorPdf.Value), StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.EnumerateFiles(output.Value, "*.pending", SearchOption.TopDirectoryOnly), _ => true);
    }

    [Fact]
    public async Task PublishAsync_keeps_previous_output_when_new_validation_fails()
    {
        Directory.CreateDirectory(rootPath);
        var output = new DirectoryReference(Path.Combine(rootPath, "Book One", "Output"));
        Directory.CreateDirectory(output.Value);
        var cover = Path.Combine(output.Value, "Book One - Cover.pdf");
        var interior = Path.Combine(output.Value, "Book One - Interior.pdf");
        await File.WriteAllTextAsync(cover, "old-cover");
        await File.WriteAllTextAsync(interior, "old-interior");
        var exported = await ExportFullAsync(await CreatePngAsync(), new DirectoryReference(Path.Combine(rootPath, "Book One", ".workspace", "output-temp")));

        await Assert.ThrowsAsync<InvalidDataException>(() => new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()).PublishAsync(
            new BookOutputPublicationRequest(new BookId("Book One"), exported, output,
                new PrintableBookPdfValidation(1, 2, new PhysicalPageSize(2, 1), new PhysicalPageSize(8.5, 8.5)))).AsTask());

        Assert.Equal("old-cover", await File.ReadAllTextAsync(cover));
        Assert.Equal("old-interior", await File.ReadAllTextAsync(interior));
    }

    [Fact]
    public async Task PublishInteriorAsync_replaces_only_the_interior_pdf()
    {
        Directory.CreateDirectory(rootPath);
        var image = await CreatePngAsync();
        var output = new DirectoryReference(Path.Combine(rootPath, "Book One", "Output"));
        Directory.CreateDirectory(output.Value);
        var cover = Path.Combine(output.Value, "Book One - Cover.pdf");
        var interior = Path.Combine(output.Value, "Book One - Interior.pdf");
        await File.WriteAllTextAsync(cover, "old-cover");
        await File.WriteAllTextAsync(interior, "old-interior");
        var temporaryOutput = new DirectoryReference(Path.Combine(rootPath, "Book One", ".workspace", "output-temp"));
        var exported = await new MagickPrintableBookPdfExporter().ExportInteriorAsync(
            new InteriorPdfExportRequest([image], temporaryOutput, new PhysicalPageSize(8.5, 8.5)));

        var published = await new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()).PublishInteriorAsync(
            new InteriorOutputPublicationRequest(new BookId("Book One"), exported, output, 1, new PhysicalPageSize(8.5, 8.5)));

        Assert.Equal("old-cover", await File.ReadAllTextAsync(cover));
        Assert.Equal(Path.Combine(output.Value, "Book One - Interior.pdf"), published.InteriorPdf.Value);
        Assert.Equal(output, published.PublishedDirectory);
        Assert.StartsWith("%PDF", await File.ReadAllTextAsync(published.InteriorPdf.Value), StringComparison.Ordinal);
    }

    private static ValueTask<PrintableBookPdfExportResult> ExportFullAsync(FileReference image, DirectoryReference temporaryOutput) =>
        new MagickPrintableBookPdfExporter().ExportAsync(new PrintableBookPdfExportRequest(
            image, [image], temporaryOutput, new PhysicalPageSize(2, 1), new PhysicalPageSize(8.5, 8.5)));

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
