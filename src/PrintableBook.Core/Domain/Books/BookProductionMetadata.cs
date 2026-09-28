using System.Globalization;

namespace PrintableBook.Core.Domain.Books;

public sealed record BookProductionMetadata(
    string? Title,
    string? Subtitle,
    string? Subcover,
    string? Description,
    string? Author)
{
    public IReadOnlyList<BookMetadataValidationError> ValidateForSave()
    {
        var errors = new List<BookMetadataValidationError>();

        ValidateSingleLine("title", "Title", Title, errors);
        ValidateTerms("title", "Title", Title, 2, 3, errors);
        ValidateCharacters("title", "Title", Title, 120, errors);
        ValidateDuplicates("title", "Title", Title, errors);

        ValidateSingleLine("subtitle", "Subtitle", Subtitle, errors);
        ValidateCharacters("subtitle", "Subtitle", Subtitle, 120, errors);
        ValidateDuplicates("subtitle", "Subtitle", Subtitle, errors);

        ValidateSingleLine("subcover", "Subcover", Subcover, errors);
        ValidateTerms("subcover", "Subcover", Subcover, 4, 6, errors);
        ValidateCharacters("subcover", "Subcover", Subcover, 100, errors);

        ValidateSingleLine("author", "Author", Author, errors);
        return errors;
    }

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

    private static void ValidateSingleLine(string field, string label, string? value, ICollection<BookMetadataValidationError> errors)
    {
        if (EffectiveValue(value)?.IndexOfAny(['\r', '\n']) >= 0)
        {
            errors.Add(new(field, "single_line", $"{label} must be a single line."));
        }
    }

    private static void ValidateTerms(
        string field,
        string label,
        string? value,
        int minimum,
        int maximum,
        ICollection<BookMetadataValidationError> errors)
    {
        var effective = EffectiveValue(value);
        if (effective is null) return;

        var count = SplitTerms(effective).Count;
        if (count < minimum || count > maximum)
        {
            var expected = minimum == maximum
                ? $"exactly {minimum}"
                : maximum == minimum + 1
                    ? $"{minimum} or {maximum}"
                    : $"{minimum} to {maximum}";
            errors.Add(new(field, "term_count", $"{label} must contain {expected} terms (currently {count})."));
        }
    }

    private static void ValidateCharacters(
        string field,
        string label,
        string? value,
        int maximumExclusive,
        ICollection<BookMetadataValidationError> errors)
    {
        var effective = EffectiveValue(value);
        if (effective is null) return;

        var count = StringInfo.ParseCombiningCharacters(effective).Length;
        if (count >= maximumExclusive)
        {
            errors.Add(new(field, "character_limit", $"{label} must be under {maximumExclusive} characters (currently {count})."));
        }
    }

    private static void ValidateDuplicates(string field, string label, string? value, ICollection<BookMetadataValidationError> errors)
    {
        var effective = EffectiveValue(value);
        if (effective is null) return;

        var terms = SplitTerms(effective);
        var emitted = new List<(string Left, string Right)>();
        for (var leftIndex = 0; leftIndex < terms.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < terms.Count; rightIndex++)
            {
                var left = terms[leftIndex];
                var right = terms[rightIndex];
                if (!AreDuplicateTerms(left, right) || ContainsRelationship(emitted, left, right)) continue;

                emitted.Add((left, right));
                errors.Add(new(
                    field,
                    "duplicate_terms",
                    $"{label} contains duplicate terms: \"{left}\" and \"{right}\".",
                    [left, right]));
            }
        }
    }

    private static string? EffectiveValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static IReadOnlyList<string> SplitTerms(string value)
    {
        var terms = new List<string>();
        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (IsTermSeparator(value[index]))
            {
                if (start >= 0)
                {
                    terms.Add(value[start..index]);
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = index;
            }
        }

        if (start >= 0) terms.Add(value[start..]);
        return terms;
    }

    private static bool IsTermSeparator(char value) =>
        value is >= '\u0009' and <= '\u000D' or
        '\u0020' or '\u0085' or '\u00A0' or '\u1680' or
        >= '\u2000' and <= '\u200A' or
        '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000';

    private static bool AreDuplicateTerms(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return true;

        var longer = left.Length > right.Length ? left : right;
        var shorter = left.Length > right.Length ? right : left;
        return longer.Length == shorter.Length + 1 &&
               longer.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(longer[..^1], shorter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsRelationship(
        IEnumerable<(string Left, string Right)> relationships,
        string left,
        string right) =>
        relationships.Any(existing =>
            string.Equals(existing.Left, left, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Right, right, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(existing.Left, right, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Right, left, StringComparison.OrdinalIgnoreCase));
}

public sealed record BookMetadataValidationError(
    string Field,
    string Code,
    string Message,
    IReadOnlyList<string>? Tokens = null);
