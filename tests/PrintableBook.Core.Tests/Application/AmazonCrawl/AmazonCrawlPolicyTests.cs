using PrintableBook.Core.Application.AmazonCrawl;

namespace PrintableBook.Core.Tests.Application.AmazonCrawl;

public sealed class AmazonCrawlPolicyTests
{
    [Fact]
    public void NormalizeKeywords_trims_collapses_deduplicates_and_preserves_order()
    {
        var result = AmazonCrawlPolicy.NormalizeKeywords(["  cozy   cats ", "COZY CATS", "adult coloring"]);

        Assert.Equal(["cozy cats", "adult coloring"], result);
    }

    [Fact]
    public void NormalizeKeywords_rejects_empty_too_many_and_oversized_inputs()
    {
        Assert.Equal("amazon_keywords_required", Assert.Throws<AmazonCrawlValidationException>(() => AmazonCrawlPolicy.NormalizeKeywords([" "])).Code);
        Assert.Equal("amazon_keywords_too_many", Assert.Throws<AmazonCrawlValidationException>(() => AmazonCrawlPolicy.NormalizeKeywords(Enumerable.Range(0, 31).Select(index => $"key {index}"))).Code);
        Assert.Equal("amazon_keyword_too_long", Assert.Throws<AmazonCrawlValidationException>(() => AmazonCrawlPolicy.NormalizeKeywords([new string('a', 201)])).Code);
    }

    [Fact]
    public void BuildSearchUri_encodes_input_and_stays_on_allowlisted_origin()
    {
        var uri = AmazonCrawlPolicy.BuildSearchUri(AmazonMarketplaceCatalog.UnitedStates, "cats & coffee");

        Assert.True(AmazonCrawlPolicy.IsAllowedUri(AmazonMarketplaceCatalog.UnitedStates, uri));
        Assert.Equal("?k=cats%20%26%20coffee", uri.Query);
    }

    [Fact]
    public void Fingerprint_is_stable_and_order_sensitive()
    {
        Assert.Equal(AmazonCrawlPolicy.Fingerprint(AmazonMarketplaceCatalog.UnitedStates, ["one", "two"]), AmazonCrawlPolicy.Fingerprint(AmazonMarketplaceCatalog.UnitedStates, ["ONE", "TWO"]));
        Assert.NotEqual(AmazonCrawlPolicy.Fingerprint(AmazonMarketplaceCatalog.UnitedStates, ["one", "two"]), AmazonCrawlPolicy.Fingerprint(AmazonMarketplaceCatalog.UnitedStates, ["two", "one"]));
        Assert.NotEqual(AmazonCrawlPolicy.Fingerprint(AmazonMarketplaceCatalog.UnitedStates, ["one", "two"]), AmazonCrawlPolicy.Fingerprint(AmazonMarketplaceCatalog.GetByLanguage("de"), ["one", "two"]));
    }

    [Fact]
    public void Selection_uses_first_matching_unused_asin()
    {
        var parsed = new AmazonSearchParseResult(AmazonSearchPageDiagnostic.Results,
        [
            new("B000000001", "Ordinary sketch pad"),
            new("B000000002", "Cozy Coloring Books for Adults"),
            new("B000000003", "Another coloring book")
        ]);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B000000002" };

        var result = AmazonAsinSelection.Select(0, "cozy", parsed, selected, AmazonMarketplaceCatalog.UnitedStates);

        Assert.Equal(AmazonAsinKeywordStatus.Selected, result.Status);
        Assert.Equal("B000000003", result.Asin);
    }

    [Theory]
    [InlineData("A colouring book", AmazonAsinKeywordStatus.NoMatchingTitle)]
    [InlineData("A coloring booklet", AmazonAsinKeywordStatus.NoMatchingTitle)]
    [InlineData("A coloring book", AmazonAsinKeywordStatus.Selected)]
    [InlineData("Coloring books for adults", AmazonAsinKeywordStatus.Selected)]
    public void Selection_applies_exact_english_title_policy(string title, AmazonAsinKeywordStatus expected)
    {
        var parsed = new AmazonSearchParseResult(AmazonSearchPageDiagnostic.Results, [new("B000000001", title)]);

        Assert.Equal(expected, AmazonAsinSelection.Select(0, "test", parsed, new HashSet<string>(), AmazonMarketplaceCatalog.UnitedStates).Status);
    }

    [Theory]
    [InlineData("de", "Mein Malbuch für Erwachsene", AmazonAsinKeywordStatus.Selected)]
    [InlineData("fr", "Livre de coloriage relaxant", AmazonAsinKeywordStatus.Selected)]
    [InlineData("es", "Libros para colorear", AmazonAsinKeywordStatus.Selected)]
    [InlineData("it", "Libro da colorare", AmazonAsinKeywordStatus.Selected)]
    [InlineData("pt", "Livro de colorir", AmazonAsinKeywordStatus.Selected)]
    [InlineData("ja", "猫の塗り絵", AmazonAsinKeywordStatus.Selected)]
    [InlineData("nl", "Ontspannend kleurboek", AmazonAsinKeywordStatus.Selected)]
    [InlineData("de", "English Coloring Book", AmazonAsinKeywordStatus.NoMatchingTitle)]
    public void Selection_applies_the_active_market_title_terms(string languageCode, string title, AmazonAsinKeywordStatus expected)
    {
        var parsed = new AmazonSearchParseResult(AmazonSearchPageDiagnostic.Results, [new("B000000001", title)]);

        var result = AmazonAsinSelection.Select(0, "test", parsed, new HashSet<string>(), AmazonMarketplaceCatalog.GetByLanguage(languageCode));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Marketplace_catalog_covers_every_supported_language()
    {
        Assert.Equal(8, AmazonMarketplaceCatalog.All.Count);
        Assert.Equal(["en", "de", "fr", "es", "it", "pt", "ja", "nl"], AmazonMarketplaceCatalog.All.Select(profile => profile.LanguageCode));
        Assert.Equal("amazon.com.br", AmazonMarketplaceCatalog.GetByLanguage("pt").Domain);
    }
}
