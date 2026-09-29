using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.AmazonCrawl;

public static class AmazonCrawlPolicy
{
    public const int MaximumKeywords = 30;
    public const int MaximumKeywordCharacters = 200;
    public const int MaximumHtmlBytes = 5 * 1024 * 1024;
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan CrawlTimeout = TimeSpan.FromMinutes(10);
    public static readonly Uri Marketplace = new("https://www.amazon.com/");

    public static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var keywords = BookTextPolicy.NormalizePhrases(values, distinct: true);
        if (keywords.Count == 0)
        {
            throw new AmazonCrawlValidationException("amazon_keywords_required", "Enter at least one Search Keyword.");
        }
        if (keywords.Count > MaximumKeywords)
        {
            throw new AmazonCrawlValidationException("amazon_keywords_too_many", $"Search Keywords accepts at most {MaximumKeywords} phrases.");
        }

        var oversized = keywords.FirstOrDefault(value => BookTextPolicy.GraphemeCount(value) > MaximumKeywordCharacters);
        if (oversized is not null)
        {
            throw new AmazonCrawlValidationException(
                "amazon_keyword_too_long",
                $"Search Keyword '{oversized}' exceeds {MaximumKeywordCharacters} characters.");
        }
        return keywords;
    }

    public static string Fingerprint(IReadOnlyList<string> normalizedKeywords)
    {
        ArgumentNullException.ThrowIfNull(normalizedKeywords);
        var canonical = $"amazon.com\n{string.Join('\n', normalizedKeywords.Select(value => value.ToUpperInvariant()))}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static Uri BuildSearchUri(string normalizedKeyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedKeyword);
        return new UriBuilder(Marketplace) { Path = "s", Query = $"k={Uri.EscapeDataString(normalizedKeyword)}" }.Uri;
    }

    public static bool IsAllowedUri(Uri? uri) => uri is not null &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "www.amazon.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "amazon.com", StringComparison.OrdinalIgnoreCase));
}

public sealed class AmazonCrawlValidationException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public sealed record AmazonAsinCrawlRequest(
    IReadOnlyList<string> Keywords,
    string RequestFingerprint)
{
    public static AmazonAsinCrawlRequest Create(IEnumerable<string> keywords)
    {
        var normalized = AmazonCrawlPolicy.NormalizeKeywords(keywords);
        return new AmazonAsinCrawlRequest(normalized, AmazonCrawlPolicy.Fingerprint(normalized));
    }
}

public enum AmazonAsinKeywordStatus
{
    Pending,
    Searching,
    Selected,
    NoSearchResult,
    NoMatchingTitle,
    AllCandidatesUsed,
    Failed,
    Cancelled,
    NotProcessed
}

public enum AmazonAsinCrawlOutcome
{
    Idle,
    Running,
    Completed,
    Partial,
    NeedsAttention,
    Failed,
    Cancelled
}

public sealed record AmazonAsinKeywordResult(
    int InputIndex,
    string Keyword,
    AmazonAsinKeywordStatus Status,
    string? Asin = null,
    string? ReasonCode = null);

public sealed record AmazonAsinCrawlView(
    AmazonAsinCrawlOutcome Outcome,
    IReadOnlyList<AmazonAsinKeywordResult> Rows,
    int CompletedCount,
    int TotalCount,
    string FinalAsins,
    string RequestFingerprint,
    string? StopReasonCode = null)
{
    public int SelectedCount => Rows.Count(row => row.Status == AmazonAsinKeywordStatus.Selected);

    public static AmazonAsinCrawlView Pending(AmazonAsinCrawlRequest request) => new(
        AmazonAsinCrawlOutcome.Idle,
        request.Keywords.Select((keyword, index) => new AmazonAsinKeywordResult(index, keyword, AmazonAsinKeywordStatus.Pending)).ToArray(),
        0,
        request.Keywords.Count,
        string.Empty,
        request.RequestFingerprint);
}

public enum AmazonSearchPageDiagnostic
{
    Results,
    NoSearchResult,
    NeedsAttention,
    UnexpectedMarkup
}

public sealed record AmazonSearchCandidate(string Asin, string Title);

public sealed record AmazonSearchParseResult(
    AmazonSearchPageDiagnostic Diagnostic,
    IReadOnlyList<AmazonSearchCandidate> Candidates,
    string? ReasonCode = null);

public interface IAmazonSearchHtmlParser
{
    AmazonSearchParseResult Parse(string html);
}

public sealed record BrowserFetchResponse(
    int Status,
    bool Ok,
    bool Redirected,
    string FinalUrl,
    string ContentType,
    string Html);

public enum CloakBrowserState
{
    Closed,
    Checking,
    Downloading,
    Opening,
    WarmingUp,
    Ready,
    NeedsAttention,
    Error
}

public sealed record CloakBrowserStatus(CloakBrowserState State, string? ReasonCode = null);

public interface IAmazonSearchPageClient
{
    ValueTask<CloakBrowserStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    ValueTask<CloakBrowserStatus> OpenAsync(CancellationToken cancellationToken = default);
    ValueTask<BrowserFetchResponse> FetchAsync(Uri uri, CancellationToken cancellationToken = default);
}

public interface IAmazonBrowserLifetime
{
    ValueTask ShutdownAsync(CancellationToken cancellationToken = default);
}
