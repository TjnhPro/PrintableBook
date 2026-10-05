namespace PrintableBook.Core.Application.Brands;

public sealed record SupportedLanguageOption(string Code, string Name);

public static class SupportedLanguageCatalog
{
    public static IReadOnlyList<SupportedLanguageOption> All { get; } =
    [
        new("en", "English"),
        new("de", "German"),
        new("fr", "French"),
        new("es", "Spanish"),
        new("it", "Italian"),
        new("pt", "Portuguese"),
        new("nl", "Dutch")
    ];

    public static bool TryGet(string? code, out SupportedLanguageOption language)
    {
        var canonicalCode = code?.Trim();
        var match = All.FirstOrDefault(option => string.Equals(option.Code, canonicalCode, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            language = null!;
            return false;
        }

        language = match;
        return true;
    }

    public static SupportedLanguageOption GetEffective(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return All.Single(option => option.Code == "en");
        }

        return TryGet(code, out var language)
            ? language
            : throw new ArgumentException($"Unsupported language code '{code}'.", nameof(code));
    }

    public static string? NormalizeStoredCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : GetEffective(code).Code;
}

public static class LanguageEditionNamingPolicy
{
    public static string CreateDestinationName(string sourceBrandName, SupportedLanguageOption language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBrandName);
        ArgumentNullException.ThrowIfNull(language);

        var baseName = sourceBrandName;
        foreach (var option in SupportedLanguageCatalog.All)
        {
            var suffix = $"_{option.Code}";
            if (!baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            baseName = baseName[..^suffix.Length];
            break;
        }

        return $"{baseName}_{language.Code}";
    }
}
