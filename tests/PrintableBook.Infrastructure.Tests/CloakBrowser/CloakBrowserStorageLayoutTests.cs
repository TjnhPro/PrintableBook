using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Infrastructure.CloakBrowser;

namespace PrintableBook.Infrastructure.Tests.CloakBrowser;

public sealed class CloakBrowserStorageLayoutTests
{
    [Fact]
    public void Layout_keeps_profile_and_binary_cache_under_application_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "printable-book-layout");
        var layout = new CloakBrowserStorageLayout(root);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), ".cloakbrowser"), layout.Root);
        Assert.Equal(Path.Combine(layout.Root, "profile-v1"), layout.Profile);
        Assert.Equal(Path.Combine(layout.Root, "cache"), layout.Cache);
    }

    [Fact]
    public void EnsureWritable_creates_reusable_profile_and_cache_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"printable-book-cloak-{Guid.NewGuid():N}");
        try
        {
            var layout = new CloakBrowserStorageLayout(root);

            layout.EnsureWritable(Profile());
            layout.EnsureWritable(Profile());

            Assert.True(Directory.Exists(layout.Profile));
            Assert.True(Directory.Exists(layout.Cache));
            Assert.Empty(Directory.EnumerateFiles(layout.Root, ".write-*"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Layout_keeps_US_legacy_profile_and_isolates_other_markets()
    {
        var root = Path.Combine(Path.GetTempPath(), "printable-book-layout");
        var layout = new CloakBrowserStorageLayout(root);

        Assert.Equal(layout.Profile, layout.ProfileFor(Profile()));
        Assert.Equal(Path.Combine(layout.Root, "profile-de-v1"), layout.ProfileFor(Profile("de")));
        Assert.NotEqual(layout.ProfileFor(Profile("de")), layout.ProfileFor(Profile("fr")));
    }

    [Fact]
    public void Layout_uses_the_configured_profile_key()
    {
        var root = Path.Combine(Path.GetTempPath(), "printable-book-layout");
        var layout = new CloakBrowserStorageLayout(root);
        var configured = Profile("de") with { MarketCode = "de-books" };

        Assert.Equal(Path.Combine(layout.Root, "profile-de-books-v1"), layout.ProfileFor(configured));
    }

    [Fact]
    public void Browser_module_has_no_amazon_parser_or_book_dependency()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "PrintableBook.Infrastructure", "CloakBrowser", "CloakBrowserAmazonPageClient.cs"));

        Assert.DoesNotContain("AmazonSearchHtmlParser", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Domain.Books", source, StringComparison.Ordinal);
        Assert.Contains("credentials: 'include'", source, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify", source, StringComparison.Ordinal);
        Assert.Contains("EvaluateAsync<string>", source, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Deserialize<FetchPayload>", source, StringComparison.Ordinal);
        Assert.Contains("AmazonCrawlPolicy.IsAllowedUri(profile, TryUri(page.Url))", source, StringComparison.Ordinal);
        Assert.Contains("AmazonCrawlPolicy.IsAllowedUri(profile, TryUri(payload.FinalUrl))", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintableBook.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static AmazonMarketplaceProfile Profile(string languageCode = "en") =>
        AmazonMarketplaceProfilePolicy.Resolve(GlobalSettings.Default, languageCode);
}
