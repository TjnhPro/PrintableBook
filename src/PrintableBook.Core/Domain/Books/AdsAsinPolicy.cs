using System.Text.RegularExpressions;

namespace PrintableBook.Core.Domain.Books;

public static partial class AdsAsinPolicy
{
    public const int MaximumTargets = 1000;
    public const int MaximumTargetCharacters = 200;
    public const int MaximumUtf8Bytes = 64 * 1024;

    public static IReadOnlyList<string> Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = value
            .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(target => target.Length > 0)
            .Select(target => target.Normalize())
            .Where(seen.Add)
            .ToArray();
        ValidateBounds(targets);
        return targets;
    }

    public static IReadOnlyList<string> MergeCrawlerResults(string? existing, IEnumerable<string> crawlerAsins)
    {
        ArgumentNullException.ThrowIfNull(crawlerAsins);
        var result = Normalize(existing).ToList();
        var seen = result.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in crawlerAsins)
        {
            var asin = raw?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(asin) || !CrawlerAsinRegex().IsMatch(asin)) continue;
            if (seen.Add(asin)) result.Add(asin);
        }
        ValidateBounds(result);
        return result;
    }

    public static string? Join(IReadOnlyList<string> targets) => targets.Count == 0 ? null : string.Join(',', targets);

    private static void ValidateBounds(IReadOnlyList<string> targets)
    {
        if (targets.Count > MaximumTargets) throw new BookKeywordBuilderValidationException(new(
            "ads_asin_too_many", $"Ads ASIN accepts at most {MaximumTargets} targets."));
        var oversized = targets.FirstOrDefault(target => BookTextPolicy.GraphemeCount(target) > MaximumTargetCharacters);
        if (oversized is not null) throw new BookKeywordBuilderValidationException(new(
            "ads_asin_target_too_long", $"Ads ASIN target '{oversized}' exceeds {MaximumTargetCharacters} characters."));
        if (System.Text.Encoding.UTF8.GetByteCount(string.Join(',', targets)) > MaximumUtf8Bytes) throw new BookKeywordBuilderValidationException(new(
            "ads_asin_too_large", $"Ads ASIN accepts at most {MaximumUtf8Bytes} UTF-8 bytes."));
    }

    [GeneratedRegex("^[A-Z0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex CrawlerAsinRegex();
}
