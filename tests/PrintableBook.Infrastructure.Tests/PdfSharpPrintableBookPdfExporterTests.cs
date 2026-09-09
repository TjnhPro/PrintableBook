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
    public async Task ExportAsync_writes_final_interior_rasters_at_their_density_derived_physical_size()
    {
        Directory.CreateDirectory(rootPath);
        var cover = await CreatePngAsync("cover.png", 5242, 2626);
        var pageOne = await CreatePngAsync("page-01.png", 2588, 2625);
        var pageTwo = await CreatePngAsync("page-02.png", 2588, 2625);
        var output = new DirectoryReference(Path.Combine(rootPath, "output"));

        var result = await new PdfSharpPrintableBookPdfExporter().ExportAsync(
            new PrintableBookPdfExportRequest(
                cover,
                [],
                [pageOne, pageTwo],
                null,
                output,
                new PhysicalPageSize(5242d / 300d, 2626d / 300d),
                new PhysicalPageSize(2588d / 300d, 2625d / 300d),
                MaximumPageConcurrency: 4));

        using var coverPdf = PdfReader.Open(result.CoverPdf.Value);
        using var interiorPdf = PdfReader.Open(result.InteriorPdf.Value);
        Assert.Single(coverPdf.Pages);
        Assert.Equal(2, interiorPdf.Pages.Count);
        Assert.Equal(5242d / 300d * 72d, coverPdf.Pages[0].Width.Point, precision: 3);
        Assert.Equal(2626d / 300d * 72d, coverPdf.Pages[0].Height.Point, precision: 3);
        Assert.Equal(2588d / 300d * 72d, interiorPdf.Pages[0].Width.Point, precision: 3);
        Assert.Equal(2625d / 300d * 72d, interiorPdf.Pages[0].Height.Point, precision: 3);
        Assert.Equal(2588d / 300d * 72d, interiorPdf.Pages[1].Width.Point, precision: 3);
        Assert.Equal(2625d / 300d * 72d, interiorPdf.Pages[1].Height.Point, precision: 3);
        Assert.True(new FileInfo(result.InteriorPdf.Value).Length > 0);

        var interiorBytes = await File.ReadAllBytesAsync(result.InteriorPdf.Value);
        var interiorText = System.Text.Encoding.Latin1.GetString(interiorBytes);
        Assert.Contains("/Width 2588", interiorText, StringComparison.Ordinal);
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

    [Fact]
    public async Task ExportInteriorAsync_assembles_many_background_units_in_deterministic_page_count()
    {
        Directory.CreateDirectory(rootPath);
        var artworks = new List<FileReference>();
        for (var index = 0; index < 24; index++)
        {
            artworks.Add(await CreatePngAsync($"art-{index:D2}.png", 100, 100));
        }

        var background = await CreatePngAsync("background.png", 100, 100);
        var request = new InteriorPdfExportRequest(
            IntroPages: [],
            OrderedInteriorPages: artworks,
            BackgroundPage: background,
            TemporaryOutputDirectory: new DirectoryReference(Path.Combine(rootPath, "many-output")),
            InteriorPageSize: new PhysicalPageSize(8.5, 8.5),
            MaximumPageConcurrency: 6);
        var exporter = new PdfSharpPrintableBookPdfExporter();

        var first = await exporter.ExportInteriorAsync(request);
        using (var firstPdf = PdfReader.Open(first.InteriorPdf.Value))
        {
            Assert.Equal(48, firstPdf.Pages.Count);
        }

        var second = await exporter.ExportInteriorAsync(request with
        {
            TemporaryOutputDirectory = new DirectoryReference(Path.Combine(rootPath, "many-output-second"))
        });
        using var secondPdf = PdfReader.Open(second.InteriorPdf.Value);
        Assert.Equal(48, secondPdf.Pages.Count);
    }

    [Fact]
    public async Task ExportInteriorAsync_rejects_non_positive_concurrency()
    {
        Directory.CreateDirectory(rootPath);
        var page = await CreatePngAsync("art.png");
        var request = new InteriorPdfExportRequest(
            [],
            [page],
            null,
            new DirectoryReference(Path.Combine(rootPath, "invalid-concurrency-output")),
            new PhysicalPageSize(8.5, 8.5),
            MaximumPageConcurrency: 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new PdfSharpPrintableBookPdfExporter().ExportInteriorAsync(
                request with { MaximumPageConcurrency = 0 }).AsTask());
    }

    [Fact]
    public async Task ExportInteriorAsync_does_not_create_output_when_cancelled_before_start()
    {
        Directory.CreateDirectory(rootPath);
        var page = await CreatePngAsync("art.png");
        var request = new InteriorPdfExportRequest(
            [],
            [page],
            null,
            new DirectoryReference(Path.Combine(rootPath, "cancelled-output")),
            new PhysicalPageSize(8.5, 8.5),
            MaximumPageConcurrency: 4);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new PdfSharpPrintableBookPdfExporter().ExportInteriorAsync(
                request,
                cancellation.Token).AsTask());

        Assert.False(File.Exists(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf")));
    }

    [Fact]
    public async Task ExportInteriorAsync_releases_partial_worker_resources_when_an_artwork_is_missing()
    {
        Directory.CreateDirectory(rootPath);
        var first = await CreatePngAsync("first.png", 100, 100);
        var last = await CreatePngAsync("last.png", 100, 100);
        var output = new DirectoryReference(Path.Combine(rootPath, "partial-failure-output"));
        var request = new InteriorPdfExportRequest(
            [],
            [first, new FileReference(Path.Combine(rootPath, "missing.png")), last],
            null,
            output,
            new PhysicalPageSize(8.5, 8.5),
            MaximumPageConcurrency: 4);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => new PdfSharpPrintableBookPdfExporter().ExportInteriorAsync(request).AsTask());

        Assert.False(File.Exists(Path.Combine(output.Value, "interior.pdf")));
        Directory.Delete(output.Value, recursive: true);
    }

    [Fact]
    public async Task ExportInteriorAsync_preserves_monochrome_png_artwork_that_pdfsharp_core_cannot_load_directly()
    {
        Directory.CreateDirectory(rootPath);
        var page = await CreateMonochromePngAsync("monochrome.png");
        var result = await new PdfSharpPrintableBookPdfExporter().ExportInteriorAsync(
            new InteriorPdfExportRequest(
                [],
                [page],
                null,
                new DirectoryReference(Path.Combine(rootPath, "monochrome-output")),
                new PhysicalPageSize(8.5, 8.5),
                MaximumPageConcurrency: 1));

        using var pdf = PdfReader.Open(result.InteriorPdf.Value);
        Assert.Single(pdf.Pages);
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

    private async Task<FileReference> CreateMonochromePngAsync(string filename)
    {
        var path = Path.Combine(rootPath, filename);
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
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
