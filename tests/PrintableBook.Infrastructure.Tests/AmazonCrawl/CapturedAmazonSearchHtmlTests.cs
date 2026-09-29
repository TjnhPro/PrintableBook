using System.Text.RegularExpressions;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Infrastructure.AmazonCrawl;

namespace PrintableBook.Infrastructure.Tests.AmazonCrawl;

[Trait(InfrastructureTestScopes.TraitName, InfrastructureTestScopes.CapturedAmazonHtml)]
public sealed class CapturedAmazonSearchHtmlTests
{
    [CapturedAmazonHtmlFact]
    public void Keyword_capture_has_a_valid_searchbox_and_exact_asin_title_pairs()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "screenshots", "keyword.html");
        Assert.True(File.Exists(path), $"Captured fixture is missing: {path}");

        var result = new AmazonSearchHtmlParser().Parse(File.ReadAllText(path));

        Assert.Equal(AmazonSearchPageDiagnostic.Results, result.Diagnostic);
        Assert.Null(result.ReasonCode);
        Assert.Equal(57, result.Candidates.Count);
        Assert.Equal(result.Candidates.Count, result.Candidates.Select(candidate => candidate.Asin).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(result.Candidates, candidate =>
        {
            Assert.Matches(new Regex("^[A-Z0-9]{10}$", RegexOptions.CultureInvariant), candidate.Asin);
            Assert.False(string.IsNullOrWhiteSpace(candidate.Title));
        });

        AssertPair(result, "B0GV4WBYDP", "Big Monster Trucks Coloring Book: 50 Easy and Fun Coloring Pages for Kids Ages 3 and Up (Q8 Coloring Cars Series)");
        AssertPair(result, "B0DQSHRG96", "Cozy Cuties: Coloring Book for Adults and Kids (Cozy Spaces Coloring)");
        AssertPair(result, "1079520015", "100 Amazing Patterns: An Adult Coloring Book with Fun, Easy, and Relaxing Coloring Pages");
        AssertPair(result, "B0DY324Q92", "Crayola Giant Pokemon Coloring Book (288 Pages), 2 Sticker Sheets, Coloring Book for Kids & Adults, Travel Activity, Pokemon Gift for Kids, 3+");
        AssertPair(result, "1523515279", "The Reverse Coloring Book™: The Book Has the Colors, You Draw the Lines!");
        Assert.Equal("B0GV4WBYDP", result.Candidates[0].Asin);
        Assert.Equal("1523515279", result.Candidates[^1].Asin);
    }

    private static void AssertPair(AmazonSearchParseResult result, string asin, string expectedTitle)
    {
        var candidate = Assert.Single(result.Candidates, item => item.Asin == asin);
        Assert.Equal(expectedTitle, candidate.Title);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintableBook.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
