using System.Text;
using System.Text.RegularExpressions;

namespace PrintableBook.Core.Application.AmazonCrawl;

public sealed record AmazonMarketplaceSettings(
    string ProfileKey,
    string BaseUrl,
    string Locale,
    string TitleTerms);

public sealed record AmazonMarketplaceProfile(
    string MarketCode,
    string LanguageCode,
    string MarketName,
    Uri MarketplaceUri,
    string Locale,
    IReadOnlyList<string> TitleTerms,
    IReadOnlyList<string> SponsoredMarkers,
    IReadOnlyList<string> NoResultMarkers,
    IReadOnlyList<string> ChallengeMarkers)
{
    public string Domain => MarketplaceUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
        ? MarketplaceUri.Host[4..]
        : MarketplaceUri.Host;

    public bool MatchesTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var normalizedTitle = Normalize(title);
        if (LanguageCode == "ja")
        {
            return TitleTerms.Any(term => normalizedTitle.Contains(Normalize(term), StringComparison.OrdinalIgnoreCase));
        }

        return TitleTerms.Any(term => Regex.IsMatch(
            normalizedTitle,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(Normalize(term)).Replace("\\ ", @"\s+")}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    public bool ContainsMarker(string value, IReadOnlyList<string> markers)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(markers);
        var normalized = Normalize(value);
        return markers.Any(marker => normalized.Contains(Normalize(marker), StringComparison.OrdinalIgnoreCase));
    }

    internal static string Normalize(string value) => string.Join(' ',
        value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
