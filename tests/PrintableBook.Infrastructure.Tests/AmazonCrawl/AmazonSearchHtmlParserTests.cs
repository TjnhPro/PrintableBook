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
              <input id="twotabsearchtextbox" type="text">
              <div data-component-type="s-search-result" data-asin="b000000001"><span class="a-size-base-plus a-color-base a-text-normal">Cozy &amp; Cute Coloring Book</span></div>
              <div data-component-type="s-search-result" data-asin="B000000001"><span class="a-size-base-plus a-color-base a-text-normal">Duplicate</span></div>
              <div data-component-type="s-search-result" data-asin="bad"><span class="a-size-base-plus a-color-base a-text-normal">Invalid</span></div>
              <div data-component-type="s-search-result" data-asin="B000000002"><div data-cy="title-recipe"><h2 aria-label="Adult Coloring Books"></h2></div></div>
            </body></html>
            """;

        var result = parser.Parse(html);

        Assert.Equal(AmazonSearchPageDiagnostic.Results, result.Diagnostic);
        Assert.Equal(["B000000001", "B000000002"], result.Candidates.Select(item => item.Asin));
        Assert.Equal("Cozy & Cute Coloring Book", result.Candidates[0].Title);
    }

    [Fact]
    public void Parse_excludes_sponsored_result_cards()
    {
        const string html = """
            <html><body>
              <input id="twotabsearchtextbox" type="text">
              <div class="s-result-item" data-component-type="s-search-result" data-asin="B000000001">
                <span class="a-size-base-plus a-color-base a-text-normal">Paid Coloring Book</span>
                <span>Sponsored</span>
              </div>
              <div class="s-result-item AdHolder" data-component-type="s-search-result" data-asin="B000000001">
                <span class="a-size-base-plus a-color-base a-text-normal">Organic Coloring Book</span>
              </div>
            </body></html>
            """;

        var result = parser.Parse(html);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("B000000001", candidate.Asin);
        Assert.Equal("Organic Coloring Book", candidate.Title);
    }

    [Theory]
    [InlineData("<span class='a-size-base-plus a-color-base a-text-normal'>First Title</span>", "First Title")]
    [InlineData("<span class='a-size-medium a-color-base a-text-normal'>Second Title</span>", "Second Title")]
    [InlineData("<h2 class='a-size-base-plus a-spacing-none a-color-base a-text-normal' aria-label='Third Title'></h2>", "Third Title")]
    [InlineData("<div data-cy='title-recipe'><h2 aria-label='Fourth Title'><span>Ignored text</span></h2></div>", "Fourth Title")]
    public void Parse_uses_the_verified_title_fallbacks(string titleMarkup, string expectedTitle)
    {
        var html = $"<html><body><input id='twotabsearchtextbox'><div data-component-type='s-search-result' data-asin='B000000001'>{titleMarkup}</div></body></html>";

        var candidate = Assert.Single(parser.Parse(html).Candidates);

        Assert.Equal(expectedTitle, candidate.Title);
    }

    [Theory]
    [InlineData("<html><body>Sorry, we just need to make sure you're not a robot. Enter the characters you see below.</body></html>", AmazonSearchPageDiagnostic.NeedsAttention)]
    [InlineData("<html><body><input id='twotabsearchtextbox'>No results for your search. Try checking your spelling.</body></html>", AmazonSearchPageDiagnostic.NoSearchResult)]
    [InlineData("<html><body><input id='twotabsearchtextbox'><main>Different markup</main></body></html>", AmazonSearchPageDiagnostic.UnexpectedMarkup)]
    public void Parse_classifies_terminal_page_shapes(string html, AmazonSearchPageDiagnostic expected)
    {
        Assert.Equal(expected, parser.Parse(html).Diagnostic);
    }

    [Theory]
    [InlineData("<html><body><div data-component-type='s-search-result' data-asin='B000000001'><h2><span>Coloring Book</span></h2></div></body></html>")]
    [InlineData("<html><body><script>const marker = 'id=\"twotabsearchtextbox\"';</script><div data-component-type='s-search-result' data-asin='B000000001'><h2><span>Coloring Book</span></h2></div></body></html>")]
    public void Parse_rejects_result_cards_when_the_amazon_searchbox_element_is_missing(string html)
    {
        var result = parser.Parse(html);

        Assert.Equal(AmazonSearchPageDiagnostic.UnexpectedMarkup, result.Diagnostic);
        Assert.Equal("amazon_searchbox_missing", result.ReasonCode);
        Assert.Empty(result.Candidates);
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
