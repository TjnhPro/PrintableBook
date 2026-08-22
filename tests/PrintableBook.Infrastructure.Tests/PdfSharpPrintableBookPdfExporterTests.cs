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
    public async Task ExportAsync_writes_cover_and_interiors_at_their_independently_configured_physical_sizes()
    {
        Directory.CreateDirectory(rootPath);
        var cover = await CreatePngAsync("cover.png", 5242, 2626);
        var pageOne = await CreatePngAsync("page-01.png");
        var pageTwo = await CreatePngAsync("page-02.png");
        var output = new DirectoryReference(Path.Combine(rootPath, "output"));

        var result = await new MagickPrintableBookPdfExporter().ExportAsync(new PrintableBookPdfExportRequest(
            cover,
            [pageOne, pageTwo],
            output,
            new PhysicalPageSize(5242d / 300d, 2626d / 300d),
            new PhysicalPageSize(8.5, 8.5)));

        using var coverPdf = PdfReader.Open(result.CoverPdf.Value);
        using var interiorPdf = PdfReader.Open(result.InteriorPdf.Value);
        using var coverRaster = new MagickImage(cover.Value);
        using var interiorRaster = new MagickImage(pageOne.Value);
        Assert.Single(coverPdf.Pages);
        Assert.Equal(2, interiorPdf.Pages.Count);
        Assert.Equal((uint)5242, coverRaster.Width);
        Assert.Equal((uint)2626, coverRaster.Height);
        Assert.Equal((uint)2550, interiorRaster.Width);
        Assert.Equal((uint)2550, interiorRaster.Height);
        Assert.Equal(5242d / 300d * 72d, coverPdf.Pages[0].Width.Point, precision: 3);
        Assert.Equal(2626d / 300d * 72d, coverPdf.Pages[0].Height.Point, precision: 3);
        Assert.NotEqual(coverPdf.Pages[0].Width.Point, interiorPdf.Pages[0].Width.Point);
        Assert.Equal(612, interiorPdf.Pages[1].Width.Point, precision: 3);
        Assert.Equal(612, interiorPdf.Pages[1].Height.Point, precision: 3);
        Assert.True(new FileInfo(result.InteriorPdf.Value).Length > 0);
        Assert.Contains("/Width 2550", await File.ReadAllTextAsync(result.InteriorPdf.Value));
    }

    private async Task<FileReference> CreatePngAsync(string filename, uint width = 2550, uint height = 2550)
    {
        var path = Path.Combine(rootPath, filename);
        using (var image = new MagickImage(MagickColors.White, width, height))
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
