using System.Text.RegularExpressions;

namespace PrintableBook.Core.Application.AmazonCrawl;

public static partial class AmazonAsinSelection
{
    public static AmazonAsinKeywordResult Select(
        int inputIndex,
        string keyword,
        AmazonSearchParseResult parsed,
        ISet<string> selected)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(selected);

        if (parsed.Diagnostic == AmazonSearchPageDiagnostic.NeedsAttention)
        {
            return new(inputIndex, keyword, AmazonAsinKeywordStatus.Failed, ReasonCode: parsed.ReasonCode ?? "amazon_challenge_detected");
        }
        if (parsed.Diagnostic == AmazonSearchPageDiagnostic.UnexpectedMarkup)
        {
            return new(inputIndex, keyword, AmazonAsinKeywordStatus.Failed, ReasonCode: parsed.ReasonCode ?? "amazon_markup_unsupported");
        }
        if (parsed.Diagnostic == AmazonSearchPageDiagnostic.NoSearchResult)
        {
            return new(inputIndex, keyword, AmazonAsinKeywordStatus.NoSearchResult, ReasonCode: "amazon_no_search_result");
        }

        var matches = parsed.Candidates.Where(candidate => ColoringBookTitle().IsMatch(candidate.Title)).ToArray();
        if (matches.Length == 0)
        {
            return new(inputIndex, keyword, AmazonAsinKeywordStatus.NoMatchingTitle, ReasonCode: "amazon_no_matching_title");
        }

        var candidate = matches.FirstOrDefault(item => !selected.Contains(item.Asin));
        if (candidate is null)
        {
            return new(inputIndex, keyword, AmazonAsinKeywordStatus.AllCandidatesUsed, ReasonCode: "amazon_all_candidates_used");
        }

        selected.Add(candidate.Asin);
        return new(inputIndex, keyword, AmazonAsinKeywordStatus.Selected, candidate.Asin);
    }

    [GeneratedRegex(@"\bcoloring\s+books?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColoringBookTitle();
}
