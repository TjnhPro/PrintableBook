namespace PrintableBook.Core.Application.AmazonCrawl;

internal sealed record AmazonSearchDiagnosticMarkers(
    IReadOnlyList<string> Sponsored,
    IReadOnlyList<string> NoResult,
    IReadOnlyList<string> Challenge);

internal static class AmazonSearchDiagnosticPolicy
{
    public static AmazonSearchDiagnosticMarkers ForLanguage(string languageCode) => languageCode switch
    {
        "de" => Markers(["Gesponsert"], ["Keine Ergebnisse für", "keine Produkte", "Rechtschreibung"], ["Robot Check", "Geben Sie die Zeichen ein", "automatisierter Zugriff", "Zugriff verweigert"]),
        "fr" => Markers(["Sponsorisé"], ["Aucun résultat pour", "aucun produit", "orthographe"], ["contrôle robot", "saisissez les caractères", "accès automatisé", "accès refusé"]),
        "es" => Markers(["Patrocinado"], ["No hay resultados para", "ningún producto", "ortografía"], ["comprobación de robot", "introduce los caracteres", "acceso automatizado", "acceso denegado"]),
        "it" => Markers(["Sponsorizzato"], ["Nessun risultato per", "nessun prodotto", "ortografia"], ["controllo robot", "inserisci i caratteri", "accesso automatizzato", "accesso negato"]),
        "pt" => Markers(["Patrocinado"], ["Nenhum resultado para", "nenhum produto", "ortografia"], ["verificação de robô", "digite os caracteres", "acesso automatizado", "acesso negado"]),
        "ja" => Markers(["スポンサー"], ["検索結果", "商品が見つかりません"], ["ロボット", "表示されている文字", "自動アクセス", "アクセスが拒否"]),
        "nl" => Markers(["Gesponsord"], ["Geen resultaten voor", "geen producten", "spelling"], ["robotcontrole", "voer de tekens in", "geautomatiseerde toegang", "toegang geweigerd"]),
        _ => Markers(["Sponsored"], ["No results for", "did not match any products", "Try checking your spelling"], ["robot check", "enter the characters you see below", "automated access", "access denied"])
    };

    private static AmazonSearchDiagnosticMarkers Markers(IReadOnlyList<string> sponsored, IReadOnlyList<string> noResult, IReadOnlyList<string> challenge) =>
        new(sponsored, noResult, challenge);
}
