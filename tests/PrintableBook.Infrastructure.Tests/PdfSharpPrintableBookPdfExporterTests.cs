using ImageMagick;
using PdfSharp.Pdf.IO;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Pdf;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PdfSharpPrintableBookPdfExporterTests : IAsyncLifetime
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

        var result = await new PdfSharpPrintableBookPdfExporter().ExportAsync(
            new PrintableBookPdfExportRequest(
                cover,
                [],
                [pageOne, pageTwo],
                null,
                output,
                new PhysicalPageSize(5242d / 300d, 2626d / 300d),
                new PhysicalPageSize(8.5, 8.5),
                MaximumPageConcurrency: 4));

        using var coverPdf = PdfReader.Open(result.CoverPdf.Value);
        using var interiorPdf = PdfReader.Open(result.InteriorPdf.Value);
        Assert.Single(coverPdf.Pages);
        Assert.Equal(2, interiorPdf.Pages.Count);
        Assert.Equal(5242d / 300d * 72d, coverPdf.Pages[0].Width.Point, precision: 3);
        Assert.Equal(2626d / 300d * 72d, coverPdf.Pages[0].Height.Point, precision: 3);
        Assert.Equal(612, interiorPdf.Pages[0].Width.Point, precision: 3);
        Assert.Equal(612, interiorPdf.Pages[0].Height.Point, precision: 3);
        Assert.Equal(612, interiorPdf.Pages[1].Width.Point, precision: 3);
        Assert.Equal(612, interiorPdf.Pages[1].Height.Point, precision: 3);
        Assert.True(new FileInfo(result.InteriorPdf.Value).Length > 0);

        var interiorBytes = await File.ReadAllBytesAsync(result.InteriorPdf.Value);
        var interiorText = System.Text.Encoding.Latin1.GetString(interiorBytes);
        Assert.Contains("/Width 2550", interiorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportInteriorAsync_writes_a_repeated_background_reference_as_distinct_pdf_pages()
    {
        Directory.CreateDirectory(rootPath);
        var pageOne = await CreatePngAsync("art-01.png");
        var pageTwo = await CreatePngAsync("art-02.png");
        var background = await CreatePngAsync("background.png");
        var output = new DirectoryReference(Path.Combine(rootPath, "repeated-background-output"));

        var result = await new PdfSharpPrintableBookPdfExporter().ExportInteriorAsync(
            new InteriorPdfExportRequest(
                IntroPages: [],
                OrderedInteriorPages: [pageOne, pageTwo],
                BackgroundPage: background,
                TemporaryOutputDirectory: output,
                InteriorPageSize: new PhysicalPageSize(8.5, 8.5),
                MaximumPageConcurrency: 4));

        using var interiorPdf = PdfReader.Open(result.InteriorPdf.Value);
        Assert.Equal(4, interiorPdf.Pages.Count);
    }

    [Fact]
    public async Task ExportInteriorAsync_keeps_intro_pages_before_artwork_when_background_is_present()
    {
        Directory.CreateDirectory(rootPath);
        var introOne = await CreatePngAsync("intro-01.png");
        var introTwo = await CreatePngAsync("intro-02.png");
        var pageOne = await CreatePngAsync("art-01.png");
        var pageTwo = await CreatePngAsync("art-02.png");
        var background = await CreatePngAsync("background.png");
        var output = new DirectoryReference(Path.Combine(rootPath, "intro-background-output"));

        var result = await new PdfSharpPrintableBookPdfExporter().ExportInteriorAsync(
            new InteriorPdfExportRequest(
                IntroPages: [introOne, introTwo],
                OrderedInteriorPages: [pageOne, pageTwo],
                BackgroundPage: background,
                TemporaryOutputDirectory: output,
                InteriorPageSize: new PhysicalPageSize(8.5, 8.5),
                MaximumPageConcurrency: 4));

        using var interiorPdf = PdfReader.Open(result.InteriorPdf.Value);
        Assert.Equal(8, interiorPdf.Pages.Count);
    }

    private async Task<FileReference> CreatePngAsync(string filename, uint width = 2550, uint height = 2550)
    {
        var path = Path.Combine(rootPath, filename);
        using (var image = new MagickImage(MagickColors.White, width, height))
        {
            image.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
            image.ColorType = ColorType.TrueColor;
            image.Format = MagickFormat.Png24;
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
