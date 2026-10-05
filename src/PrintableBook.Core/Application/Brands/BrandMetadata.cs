namespace PrintableBook.Core.Application.Brands;

public sealed record BrandMetadata(string? Author, string? LanguageCode = null)
{
    public static BrandMetadata Create(string? author, string? languageCode = null) =>
        new(NormalizeAuthor(author), SupportedLanguageCatalog.NormalizeStoredCode(languageCode));

    public BrandMetadata Normalize() => Create(Author, LanguageCode);

    public SupportedLanguageOption EffectiveLanguage => SupportedLanguageCatalog.GetEffective(LanguageCode);

    public static string? NormalizeAuthor(string? author)
    {
        var normalized = author?.Trim();
        if (normalized?.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("Author must be a single line.", nameof(author));
        }
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public static class AuthorMatchPolicy
{
    public static bool IsMatch(string? left, string? right)
    {
        var normalizedLeft = BrandMetadata.NormalizeAuthor(left);
        var normalizedRight = BrandMetadata.NormalizeAuthor(right);
        return normalizedLeft is not null &&
               normalizedRight is not null &&
               string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }
}
