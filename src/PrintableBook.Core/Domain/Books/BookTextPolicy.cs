using System.Globalization;
using System.Text;

namespace PrintableBook.Core.Domain.Books;

public static class BookTextPolicy
{
    public static int GraphemeCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return StringInfo.ParseCombiningCharacters(value).Length;
    }

    public static IReadOnlyList<string> SplitWords(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (current.Length == 0) continue;
                words.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }

    public static string? NormalizePhrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', SplitWords(value.Trim()));
    }

    public static string? NormalizeOptionalMultiline(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
