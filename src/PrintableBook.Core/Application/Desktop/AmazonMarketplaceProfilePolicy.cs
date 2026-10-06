using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.Brands;

namespace PrintableBook.Core.Application.Desktop;

public static partial class AmazonMarketplaceProfilePolicy
{
    private sealed record MarketDefault(
        string LanguageCode,
        string MarketName,
        string AllowedDomain,
        AmazonMarketplaceSettings Settings);

    private static readonly IReadOnlyList<MarketDefault> Defaults =
    [
        Default("en", "United States", "amazon.com", "us", "https://www.amazon.com/", "en-US", "coloring book, coloring books"),
        Default("de", "Germany", "amazon.de", "de", "https://www.amazon.de/", "de-DE", "Malbuch, Malbücher, Ausmalbuch, Ausmalbücher"),
        Default("fr", "France", "amazon.fr", "fr", "https://www.amazon.fr/", "fr-FR", "livre de coloriage, livres de coloriage"),
        Default("es", "Spain", "amazon.es", "es", "https://www.amazon.es/", "es-ES", "libro para colorear, libros para colorear"),
        Default("it", "Italy", "amazon.it", "it", "https://www.amazon.it/", "it-IT", "libro da colorare, libri da colorare"),
        Default("pt", "Brazil", "amazon.com.br", "br", "https://www.amazon.com.br/", "pt-BR", "livro de colorir, livros de colorir"),
        Default("ja", "Japan", "amazon.co.jp", "jp", "https://www.amazon.co.jp/", "ja-JP", "塗り絵, ぬりえ"),
        Default("nl", "Netherlands", "amazon.nl", "nl", "https://www.amazon.nl/", "nl-NL", "kleurboek, kleurboeken")
    ];

    public static IReadOnlyDictionary<string, AmazonMarketplaceSettings> DefaultProfiles { get; } =
        new ReadOnlyDictionary<string, AmazonMarketplaceSettings>(Defaults.ToDictionary(
            item => item.LanguageCode,
            item => item.Settings,
            StringComparer.Ordinal));

    public static GlobalSettings NormalizeLoaded(GlobalSettings settings) => Normalize(settings);

    public static GlobalSettings NormalizeForSave(GlobalSettings settings) => Normalize(settings);

    public static AmazonMarketplaceProfile Resolve(GlobalSettings settings, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        var language = SupportedLanguageCatalog.GetEffective(languageCode);
        var market = Defaults.Single(item => item.LanguageCode == language.Code);
        var configured = normalized.AmazonMarketplaceProfiles![language.Code];
        var markers = AmazonSearchDiagnosticPolicy.ForLanguage(language.Code);
        return new AmazonMarketplaceProfile(
            configured.ProfileKey,
            language.Code,
            market.MarketName,
            new Uri(configured.BaseUrl, UriKind.Absolute),
            configured.Locale,
            SplitTitleTerms(configured.TitleTerms),
            markers.Sponsored,
            markers.NoResult,
            markers.Challenge);
    }

    private static GlobalSettings Normalize(GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var source = settings.AmazonMarketplaceProfiles;
        if (source is null)
        {
            return settings with { AmazonMarketplaceProfiles = DefaultProfiles };
        }

        var byLanguage = new Dictionary<string, AmazonMarketplaceSettings>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            if (!SupportedLanguageCatalog.TryGet(pair.Key, out var language))
            {
                throw new ArgumentException($"Unsupported Amazon marketplace language code '{pair.Key}'.", nameof(settings));
            }
            if (!byLanguage.TryAdd(language.Code, NormalizeProfile(language.Code, pair.Value)))
            {
                throw new ArgumentException($"Duplicate Amazon marketplace language code '{language.Code}'.", nameof(settings));
            }
        }

        foreach (var language in SupportedLanguageCatalog.All)
        {
            if (!byLanguage.ContainsKey(language.Code))
            {
                throw new ArgumentException($"Amazon marketplace profile '{language.Code}' is required.", nameof(settings));
            }
        }

        var duplicateProfileKey = byLanguage
            .GroupBy(pair => pair.Value.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProfileKey is not null)
        {
            throw new ArgumentException($"Amazon browser profile key '{duplicateProfileKey.Key}' must be unique.", nameof(settings));
        }

        return settings with { AmazonMarketplaceProfiles = byLanguage };
    }

    private static AmazonMarketplaceSettings NormalizeProfile(string languageCode, AmazonMarketplaceSettings? profile)
    {
        if (profile is null) throw new ArgumentException($"Amazon marketplace profile '{languageCode}' is required.", nameof(profile));
        var profileKey = AmazonMarketplaceProfile.Normalize(profile.ProfileKey).ToLowerInvariant();
        if (!ProfileKeyPattern().IsMatch(profileKey))
        {
            throw new ArgumentException($"Amazon browser profile key '{profile.ProfileKey}' is invalid.", nameof(profile));
        }

        var locale = AmazonMarketplaceProfile.Normalize(profile.Locale);
        if (locale.Length == 0) throw new ArgumentException("Amazon locale is required.", nameof(profile));

        var baseUrl = NormalizeBaseUrl(languageCode, profile.BaseUrl);
        var terms = SplitTitleTerms(profile.TitleTerms);
        if (terms.Count == 0) throw new ArgumentException("At least one Amazon title term is required.", nameof(profile));

        return new AmazonMarketplaceSettings(profileKey, baseUrl, locale, string.Join(", ", terms));
    }

    private static string NormalizeBaseUrl(string languageCode, string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new ArgumentException("Amazon Base URL must be an HTTPS origin without a path, query, fragment, credentials, or custom port.", nameof(value));
        }

        var expectedDomain = Defaults.Single(item => item.LanguageCode == languageCode).AllowedDomain;
        if (!string.Equals(uri.Host, expectedDomain, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, $"www.{expectedDomain}", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Amazon Base URL for '{languageCode}' must use {expectedDomain}.", nameof(value));
        }

        return $"https://{uri.Host.ToLowerInvariant()}/";
    }

    private static IReadOnlyList<string> SplitTitleTerms(string? value) =>
        (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(AmazonMarketplaceProfile.Normalize)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static MarketDefault Default(string languageCode, string marketName, string domain, string profileKey, string baseUrl, string locale, string titleTerms) =>
        new(languageCode, marketName, domain, new AmazonMarketplaceSettings(profileKey, baseUrl, locale, titleTerms));

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileKeyPattern();
}
