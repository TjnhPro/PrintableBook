using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PrintableBook.Core.Application.AmazonCrawl;

namespace PrintableBook.Infrastructure.AmazonCrawl;

public sealed partial class AmazonSearchHtmlParser : IAmazonSearchHtmlParser
{
    private static readonly string[] TitleTextSelectors =
    [
        ".//span[contains(concat(' ', normalize-space(@class), ' '), ' a-size-base-plus ') and contains(concat(' ', normalize-space(@class), ' '), ' a-color-base ') and contains(concat(' ', normalize-space(@class), ' '), ' a-text-normal ')]",
        ".//span[contains(concat(' ', normalize-space(@class), ' '), ' a-size-medium ') and contains(concat(' ', normalize-space(@class), ' '), ' a-color-base ') and contains(concat(' ', normalize-space(@class), ' '), ' a-text-normal ')]"
    ];

    private static readonly string[] TitleAriaLabelSelectors =
    [
        ".//*[contains(concat(' ', normalize-space(@class), ' '), ' a-size-base-plus ') and contains(concat(' ', normalize-space(@class), ' '), ' a-spacing-none ') and contains(concat(' ', normalize-space(@class), ' '), ' a-color-base ') and contains(concat(' ', normalize-space(@class), ' '), ' a-text-normal ')]",
        ".//*[@data-cy='title-recipe']//h2"
    ];

    public AmazonSearchParseResult Parse(string html, AmazonMarketplaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(profile);
        if (ChallengeMarker().IsMatch(html) || profile.ContainsMarker(html, profile.ChallengeMarkers))
        {
            return new(AmazonSearchPageDiagnostic.NeedsAttention, [], "amazon_challenge_detected");
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        if (document.GetElementbyId("twotabsearchtextbox") is null)
        {
            return new(AmazonSearchPageDiagnostic.UnexpectedMarkup, [], "amazon_searchbox_missing");
        }

        var nodes = document.DocumentNode.SelectNodes("//div[@data-component-type='s-search-result' and normalize-space(@data-asin)!='']");
        if (nodes is null || nodes.Count == 0)
        {
            var text = Normalize(document.DocumentNode.InnerText);
            return KnownEmptyMarker().IsMatch(text) || profile.ContainsMarker(text, profile.NoResultMarkers)
                ? new(AmazonSearchPageDiagnostic.NoSearchResult, [])
                : new(AmazonSearchPageDiagnostic.UnexpectedMarkup, [], "amazon_markup_unsupported");
        }

        var candidates = new List<AmazonSearchCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (IsSponsored(node, profile)) continue;

            var asin = WebUtility.HtmlDecode(node.GetAttributeValue("data-asin", string.Empty)).Trim().ToUpperInvariant();
            if (!AsinPattern().IsMatch(asin) || !seen.Add(asin)) continue;

            var title = TitleTextSelectors
                .Select(selector => node.SelectSingleNode(selector))
                .Where(candidate => candidate is not null)
                .Select(candidate => Normalize(candidate!.InnerText))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? TitleAriaLabelSelectors
                    .Select(selector => node.SelectSingleNode(selector))
                    .Where(candidate => candidate is not null)
                    .Select(candidate => Normalize(candidate!.GetAttributeValue("aria-label", string.Empty)))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(title)) continue;
            candidates.Add(new AmazonSearchCandidate(asin, title));
        }

        return new(AmazonSearchPageDiagnostic.Results, candidates);
    }

    private static bool IsSponsored(HtmlNode node, AmazonMarketplaceProfile profile)
    {
        var className = node.GetAttributeValue("class", string.Empty);
        if (className.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("AdHolder", StringComparer.OrdinalIgnoreCase) ||
            node.SelectSingleNode(".//*[@data-component-type='sp-sponsored-result' or @data-ad-details or contains(concat(' ', normalize-space(@class), ' '), ' sponsored-label-info-icon ')]") is not null)
        {
            return true;
        }

        return profile.ContainsMarker(WebUtility.HtmlDecode(node.InnerText), profile.SponsoredMarkers);
    }

    private static string Normalize(string value) => string.Join(' ',
        WebUtility.HtmlDecode(value)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    [GeneratedRegex("^[A-Z0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex AsinPattern();

    [GeneratedRegex("captcha|robot check|enter the characters you see below|automated access|access denied", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChallengeMarker();

    [GeneratedRegex("no results for|did not match any products|try checking your spelling", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KnownEmptyMarker();
}
