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
        var uri = AmazonCrawlPolicy.BuildSearchUri("cats & coffee");

        Assert.True(AmazonCrawlPolicy.IsAllowedUri(uri));
        Assert.Equal("?k=cats%20%26%20coffee", uri.Query);
    }

    [Fact]
    public void Fingerprint_is_stable_and_order_sensitive()
    {
        Assert.Equal(AmazonCrawlPolicy.Fingerprint(["one", "two"]), AmazonCrawlPolicy.Fingerprint(["ONE", "TWO"]));
        Assert.NotEqual(AmazonCrawlPolicy.Fingerprint(["one", "two"]), AmazonCrawlPolicy.Fingerprint(["two", "one"]));
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

        var result = AmazonAsinSelection.Select(0, "cozy", parsed, selected);

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

        Assert.Equal(expected, AmazonAsinSelection.Select(0, "test", parsed, new HashSet<string>()).Status);
    }
}
