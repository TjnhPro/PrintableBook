using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Infrastructure.AmazonCrawl;

namespace PrintableBook.Infrastructure.Tests.AmazonCrawl;

public sealed class AmazonSearchHtmlParserTests
{
    private readonly AmazonSearchHtmlParser parser = new();

    [Fact]
    public void Parse_returns_valid_candidates_in_dom_order_and_deduplicates_asins()
    {
        const string html = """
            <html><body>
              <div data-component-type="s-search-result" data-asin="b000000001"><h2><a><span>Cozy &amp; Cute Coloring Book</span></a></h2></div>
              <div data-component-type="s-search-result" data-asin="B000000001"><h2><span>Duplicate</span></h2></div>
              <div data-component-type="s-search-result" data-asin="bad"><h2><span>Invalid</span></h2></div>
              <div data-component-type="s-search-result" data-asin="B000000002"><div data-cy="title-recipe"><span>Adult Coloring Books</span></div></div>
            </body></html>
            """;

        var result = parser.Parse(html);

        Assert.Equal(AmazonSearchPageDiagnostic.Results, result.Diagnostic);
        Assert.Equal(["B000000001", "B000000002"], result.Candidates.Select(item => item.Asin));
        Assert.Equal("Cozy & Cute Coloring Book", result.Candidates[0].Title);
    }

    [Theory]
    [InlineData("<html><body>Sorry, we just need to make sure you're not a robot. Enter the characters you see below.</body></html>", AmazonSearchPageDiagnostic.NeedsAttention)]
    [InlineData("<html><body>No results for your search. Try checking your spelling.</body></html>", AmazonSearchPageDiagnostic.NoSearchResult)]
    [InlineData("<html><body><main>Different markup</main></body></html>", AmazonSearchPageDiagnostic.UnexpectedMarkup)]
    public void Parse_classifies_terminal_page_shapes(string html, AmazonSearchPageDiagnostic expected)
    {
        Assert.Equal(expected, parser.Parse(html).Diagnostic);
    }

    [Fact]
    public void Parser_module_has_no_desktop_or_cloakbrowser_dependency()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "PrintableBook.Infrastructure", "AmazonCrawl", "AmazonSearchHtmlParser.cs"));

        Assert.DoesNotContain("PrintableBook.Desktop", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure.CloakBrowser", source, StringComparison.Ordinal);
        Assert.DoesNotContain("global::CloakBrowser", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintableBook.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
