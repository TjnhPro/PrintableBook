using System.Globalization;

namespace PrintableBook.Core.Domain.Books;

public sealed record BookProductionMetadata(
    string? Title,
    string? Subtitle,
    string? Subcover,
    string? Description,
    string? Author)
{
    public static BookProductionMetadata Create(
        string? title,
        string? subtitle,
        string? subcover,
        string? description,
        string? author)
    {
        var normalizedSubcover = NormalizeSingleLine(subcover);
        if (normalizedSubcover is not null)
        {
            var length = StringInfo.ParseCombiningCharacters(normalizedSubcover).Length;
            if (length >= 100)
            {
                throw new ArgumentException("Subcover must contain fewer than 100 characters.", nameof(subcover));
            }
        }

        return new BookProductionMetadata(
            NormalizeSingleLine(title),
            NormalizeSingleLine(subtitle),
            normalizedSubcover,
            NormalizeMultiline(description),
            NormalizeSingleLine(author));
    }

    public BookProductionMetadata Normalize() => Create(Title, Subtitle, Subcover, Description, Author);

    public static string? NormalizeSingleLine(string? value)
    {
        var normalized = value?.Trim();
        if (normalized?.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("Value must be a single line.", nameof(value));
        }
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeMultiline(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
