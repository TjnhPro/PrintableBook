using PrintableBook.Core.Application.Production;
using PrintableBook.Infrastructure.Production;

namespace PrintableBook.Infrastructure.Tests;

public sealed class FileSystemProductionPdfNameSuggestionServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.PdfNames.{Guid.NewGuid():N}");

    [Fact]
    public async Task GetAsync_uses_the_matching_sources_and_keeps_each_book_stable()
    {
        WriteSources([" cover-one ", "cover-two"], ["interior-one", "interior-two.pdf"]);
        var service = new FileSystemProductionPdfNameSuggestionService(root, _ => 0);

        var first = await service.GetAsync("Book One", regenerate: false);
        var repeated = await service.GetAsync("Book One", regenerate: false);

        Assert.Equal("cover-one.pdf", first.CoverFileName);
        Assert.Equal("interior-one.pdf", first.InteriorFileName);
        Assert.Same(first, repeated);
    }

    [Fact]
    public async Task GetAsync_regenerates_both_names_without_immediately_repeating_them()
    {
        WriteSources(["cover-one", "cover-two"], ["interior-one", "interior-two"]);
        var service = new FileSystemProductionPdfNameSuggestionService(root, _ => 0);

        var first = await service.GetAsync("Book One", regenerate: false);
        var regenerated = await service.GetAsync("Book One", regenerate: true);

        Assert.Equal("cover-two.pdf", regenerated.CoverFileName);
        Assert.Equal("interior-two.pdf", regenerated.InteriorFileName);
        Assert.NotEqual(first, regenerated);
    }

    [Theory]
    [InlineData("malco 1/4 5/16 bit")]
    [InlineData("CON")]
    [InlineData("name.")]
    [InlineData("bad\nname")]
    [InlineData("   ")]
    public void NormalizeName_rejects_invalid_windows_filename_stems(string value)
    {
        Assert.Null(FileSystemProductionPdfNameSuggestionService.NormalizeName(value));
    }

    [Theory]
    [InlineData("cover", "cover.pdf")]
    [InlineData(" cover.pdf ", "cover.pdf")]
    [InlineData("cover.PDF.pdf", "cover.pdf")]
    public void NormalizeName_returns_exactly_one_pdf_extension(string value, string expected)
    {
        Assert.Equal(expected, FileSystemProductionPdfNameSuggestionService.NormalizeName(value));
    }

    [Fact]
    public async Task GetAsync_reports_a_missing_source_without_affecting_other_production_services()
    {
        Directory.CreateDirectory(root);
        var service = new FileSystemProductionPdfNameSuggestionService(root);

        var exception = await Assert.ThrowsAsync<ProductionPdfNameSuggestionException>(
            () => service.GetAsync("Book One", regenerate: false).AsTask());

        Assert.Equal("pdf_name_source_missing", exception.Code);
        Assert.Contains(FileSystemProductionPdfNameSuggestionService.CoverKeyFileName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_reports_a_source_with_no_valid_names()
    {
        WriteSources(["CON", "bad/name", "  "], ["interior"]);
        var service = new FileSystemProductionPdfNameSuggestionService(root);

        var exception = await Assert.ThrowsAsync<ProductionPdfNameSuggestionException>(
            () => service.GetAsync("Book One", regenerate: false).AsTask());

        Assert.Equal("pdf_name_source_empty", exception.Code);
    }

    private void WriteSources(IEnumerable<string> cover, IEnumerable<string> interior)
    {
        Directory.CreateDirectory(root);
        File.WriteAllLines(Path.Combine(root, FileSystemProductionPdfNameSuggestionService.CoverKeyFileName), cover);
        File.WriteAllLines(Path.Combine(root, FileSystemProductionPdfNameSuggestionService.InteriorKeyFileName), interior);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
