using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using PrintableBook.Core.Application.Brands;

namespace PrintableBook.Core.Application.AmazonCrawl;

public sealed record AmazonMarketplaceProfile(
    string MarketCode,
    string LanguageCode,
    string MarketName,
    string Domain,
    string Locale,
    IReadOnlyList<string> TitleTerms,
    IReadOnlyList<string> SponsoredMarkers,
    IReadOnlyList<string> NoResultMarkers,
    IReadOnlyList<string> ChallengeMarkers)
{
    public Uri MarketplaceUri { get; } = new($"https://www.{Domain}/");

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

    private static string Normalize(string value) => string.Join(' ',
        value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

public static class AmazonMarketplaceCatalog
{
    public static IReadOnlyList<AmazonMarketplaceProfile> All { get; } = new ReadOnlyCollection<AmazonMarketplaceProfile>(
    [
        Profile("us", "en", "United States", "amazon.com", "en-US", ["coloring book", "coloring books"], ["Sponsored"], ["No results for", "did not match any products", "Try checking your spelling"], ["robot check", "enter the characters you see below", "automated access", "access denied"]),
        Profile("de", "de", "Germany", "amazon.de", "de-DE", ["Malbuch", "Malbücher", "Ausmalbuch", "Ausmalbücher"], ["Gesponsert"], ["Keine Ergebnisse für", "keine Produkte", "Rechtschreibung"], ["Robot Check", "Geben Sie die Zeichen ein", "automatisierter Zugriff", "Zugriff verweigert"]),
        Profile("fr", "fr", "France", "amazon.fr", "fr-FR", ["livre de coloriage", "livres de coloriage"], ["Sponsorisé"], ["Aucun résultat pour", "aucun produit", "orthographe"], ["contrôle robot", "saisissez les caractères", "accès automatisé", "accès refusé"]),
        Profile("es", "es", "Spain", "amazon.es", "es-ES", ["libro para colorear", "libros para colorear"], ["Patrocinado"], ["No hay resultados para", "ningún producto", "ortografía"], ["comprobación de robot", "introduce los caracteres", "acceso automatizado", "acceso denegado"]),
        Profile("it", "it", "Italy", "amazon.it", "it-IT", ["libro da colorare", "libri da colorare"], ["Sponsorizzato"], ["Nessun risultato per", "nessun prodotto", "ortografia"], ["controllo robot", "inserisci i caratteri", "accesso automatizzato", "accesso negato"]),
        Profile("br", "pt", "Brazil", "amazon.com.br", "pt-BR", ["livro de colorir", "livros de colorir"], ["Patrocinado"], ["Nenhum resultado para", "nenhum produto", "ortografia"], ["verificação de robô", "digite os caracteres", "acesso automatizado", "acesso negado"]),
        Profile("jp", "ja", "Japan", "amazon.co.jp", "ja-JP", ["塗り絵", "ぬりえ"], ["スポンサー"], ["検索結果", "商品が見つかりません"], ["ロボット", "表示されている文字", "自動アクセス", "アクセスが拒否"]),
        Profile("nl", "nl", "Netherlands", "amazon.nl", "nl-NL", ["kleurboek", "kleurboeken"], ["Gesponsord"], ["Geen resultaten voor", "geen producten", "spelling"], ["robotcontrole", "voer de tekens in", "geautomatiseerde toegang", "toegang geweigerd"])
    ]);

    public static AmazonMarketplaceProfile UnitedStates => All[0];

    public static AmazonMarketplaceProfile GetByLanguage(string? languageCode)
    {
        var language = SupportedLanguageCatalog.GetEffective(languageCode);
        return All.Single(profile => profile.LanguageCode == language.Code);
    }

    public static AmazonMarketplaceProfile GetByMarket(string marketCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketCode);
        return All.Single(profile => string.Equals(profile.MarketCode, marketCode.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static AmazonMarketplaceProfile Profile(
        string marketCode,
        string languageCode,
        string marketName,
        string domain,
        string locale,
        IReadOnlyList<string> titleTerms,
        IReadOnlyList<string> sponsoredMarkers,
        IReadOnlyList<string> noResultMarkers,
        IReadOnlyList<string> challengeMarkers) =>
        new(marketCode, languageCode, marketName, domain, locale, titleTerms, sponsoredMarkers, noResultMarkers, challengeMarkers);
}
