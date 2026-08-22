using ImageMagick;
using PdfSharp.Pdf.IO;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Pdf;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickPrintableBookPdfExporterTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.PdfTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportAsync_writes_reopenable_cover_and_ordered_interior_pdfs_at_the_configured_physical_size()
    {
        Directory.CreateDirectory(rootPath);
        var cover = await CreatePngAsync("cover.png");
        var pageOne = await CreatePngAsync("page-01.png");
        var pageTwo = await CreatePngAsync("page-02.png");
        var output = new DirectoryReference(Path.Combine(rootPath, "output"));

        var result = await new MagickPrintableBookPdfExporter().ExportAsync(new PrintableBookPdfExportRequest(
            cover,
            [pageOne, pageTwo],
            output,
            new PhysicalPageSize(8.5, 8.5)));

        using var coverPdf = PdfReader.Open(result.CoverPdf.Value);
        using var interiorPdf = PdfReader.Open(result.InteriorPdf.Value);
        Assert.Single(coverPdf.Pages);
        Assert.Equal(2, interiorPdf.Pages.Count);
        Assert.Equal(612, coverPdf.Pages[0].Width.Point, precision: 3);
        Assert.Equal(612, coverPdf.Pages[0].Height.Point, precision: 3);
        Assert.Equal(612, interiorPdf.Pages[1].Width.Point, precision: 3);
        Assert.True(new FileInfo(result.InteriorPdf.Value).Length > 0);
        Assert.Contains("/Width 2550", await File.ReadAllTextAsync(result.InteriorPdf.Value));
    }

    private async Task<FileReference> CreatePngAsync(string filename)
    {
        var path = Path.Combine(rootPath, filename);
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
