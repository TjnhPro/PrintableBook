using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.Desktop;

public static class GenericKeywordProfilePolicy
{
    public static GlobalSettings NormalizeLoaded(GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings.GenericKeywordsByLanguage);
        return Normalize(settings, preferLegacyEnglish: settings.GenericKeywords is not null);
    }

    public static GlobalSettings NormalizeForSave(GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings.GenericKeywordsByLanguage);
        return Normalize(settings, preferLegacyEnglish: false);
    }

    private static GlobalSettings Normalize(GlobalSettings settings, bool preferLegacyEnglish)
    {
        var profiles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var language in SupportedLanguageCatalog.All)
        {
            var profile = FindProfile(settings.GenericKeywordsByLanguage, language.Code);
            if (language.Code == "en" && (preferLegacyEnglish || profile is null))
            {
                profile = settings.GenericKeywords;
            }

            profiles.Add(language.Code, BookTextPolicy.NormalizePhrases(profile ?? [], distinct: true));
        }

        return settings with
        {
            GenericKeywords = profiles["en"],
            GenericKeywordsByLanguage = profiles
        };
    }

    private static IReadOnlyList<string>? FindProfile(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? profiles,
        string languageCode) =>
        profiles?.FirstOrDefault(pair => string.Equals(pair.Key, languageCode, StringComparison.OrdinalIgnoreCase)).Value;

    private static void Validate(IReadOnlyDictionary<string, IReadOnlyList<string>>? profiles)
    {
        if (profiles is null) return;
        var canonicalCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in profiles.Keys)
        {
            if (!SupportedLanguageCatalog.TryGet(code, out var language))
            {
                throw new ArgumentException($"Unsupported Generic Keywords language code '{code}'.", nameof(profiles));
            }

            if (!canonicalCodes.Add(language.Code))
            {
                throw new ArgumentException($"Duplicate Generic Keywords language code '{language.Code}'.", nameof(profiles));
            }
        }
    }
}
