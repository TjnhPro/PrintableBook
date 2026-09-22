namespace PrintableBook.Core.Application.Brands;

public sealed record BrandMetadata(string? Author)
{
    public static BrandMetadata Create(string? author) => new(NormalizeAuthor(author));

    public BrandMetadata Normalize() => Create(Author);

    public static string? NormalizeAuthor(string? author)
    {
        var normalized = author?.Trim();
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
