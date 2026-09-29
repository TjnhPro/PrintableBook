using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PrintableBook.Core.Application.AmazonCrawl;

namespace PrintableBook.Infrastructure.AmazonCrawl;

public sealed partial class AmazonSearchHtmlParser : IAmazonSearchHtmlParser
{
    private static readonly string[] TitleSelectors =
    [
        ".//h2//span",
        ".//h2/a/span",
        ".//*[@data-cy='title-recipe']//span"
    ];

    public AmazonSearchParseResult Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (ChallengeMarker().IsMatch(html))
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
            return KnownEmptyMarker().IsMatch(text)
                ? new(AmazonSearchPageDiagnostic.NoSearchResult, [])
                : new(AmazonSearchPageDiagnostic.UnexpectedMarkup, [], "amazon_markup_unsupported");
        }

        var candidates = new List<AmazonSearchCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var asin = WebUtility.HtmlDecode(node.GetAttributeValue("data-asin", string.Empty)).Trim().ToUpperInvariant();
            if (!AsinPattern().IsMatch(asin) || !seen.Add(asin)) continue;

            var title = TitleSelectors
                .Select(selector => node.SelectSingleNode(selector))
                .Where(candidate => candidate is not null)
                .Select(candidate => Normalize(candidate!.InnerText))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(title)) continue;
            candidates.Add(new AmazonSearchCandidate(asin, title));
        }

        return new(AmazonSearchPageDiagnostic.Results, candidates);
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
