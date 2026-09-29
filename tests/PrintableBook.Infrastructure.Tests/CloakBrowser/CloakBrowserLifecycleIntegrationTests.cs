using global::CloakBrowser;
using Microsoft.Playwright;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Infrastructure.AmazonCrawl;
using PrintableBook.Infrastructure.CloakBrowser;

namespace PrintableBook.Infrastructure.Tests.CloakBrowser;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class CloakBrowserLifecycleIntegrationCollection
{
    public const string CollectionName = "CloakBrowser lifecycle integration";
}

[Collection(CloakBrowserLifecycleIntegrationCollection.CollectionName)]
[Trait(InfrastructureTestScopes.TraitName, InfrastructureTestScopes.ExternalCloakBrowser)]
public sealed class CloakBrowserLifecycleIntegrationTests
{
    private const string GoogleUrl = "https://www.google.com/";
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(60);

    [ExternalCloakBrowserFact]
    public async Task Open_and_close_profile_releases_it_for_the_next_launch()
    {
        var profile = ProfilePath("open-close");

        var first = await LaunchAsync(profile);
        try
        {
            Assert.NotEmpty(first.Context.Pages);
        }
        finally
        {
            await CloseAsync(first);
        }

        var second = await LaunchAsync(profile);
        try
        {
            Assert.NotEmpty(second.Context.Pages);
        }
        finally
        {
            await CloseAsync(second);
        }

        Assert.True(Directory.Exists(profile));
    }

    [ExternalCloakBrowserFact]
    public async Task Reconnect_to_the_same_profile_preserves_profile_data()
    {
        var profile = ProfilePath("reconnect");
        const string cookieName = "printable_book_profile_probe";
        var cookieValue = Guid.NewGuid().ToString("N");

        var first = await LaunchAsync(profile);
        try
        {
            await first.Context.AddCookiesAsync(
            [
                new Cookie
                {
                    Name = cookieName,
                    Value = cookieValue,
                    Url = GoogleUrl,
                    Expires = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
                }
            ]).WaitAsync(OperationTimeout);
        }
        finally
        {
            await CloseAsync(first);
        }

        var reconnected = await LaunchAsync(profile);
        IReadOnlyList<BrowserContextCookiesResult> cookies;
        try
        {
            cookies = await reconnected.Context.CookiesAsync([GoogleUrl]).WaitAsync(OperationTimeout);
        }
        finally
        {
            await CloseAsync(reconnected);
        }

        Assert.Contains(cookies, cookie => cookie.Name == cookieName && cookie.Value == cookieValue);
    }

    [ExternalCloakBrowserFact]
    public async Task Persistent_profile_can_navigate_to_google()
    {
        var profile = ProfilePath("google");
        var handle = await LaunchAsync(profile);
        try
        {
            var page = handle.Context.Pages.FirstOrDefault() ?? await handle.NewPageAsync();
            var response = await page.GotoAsync(GoogleUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)OperationTimeout.TotalMilliseconds
            }).WaitAsync(OperationTimeout + TimeSpan.FromSeconds(5));

            Assert.NotNull(response);
            Assert.InRange(response.Status, 200, 399);
            Assert.True(Uri.TryCreate(page.Url, UriKind.Absolute, out var finalUri));
            Assert.True(
                finalUri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase) ||
                finalUri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase),
                $"Expected a Google host but reached '{finalUri.Host}'.");
        }
        finally
        {
            await CloseAsync(handle);
        }
    }

    [ExternalCloakBrowserFact]
    public async Task Amazon_fetch_returns_complete_html_as_a_json_string_payload()
    {
        var client = new CloakBrowserAmazonPageClient(
            new CloakBrowserStorageLayout(Path.Combine(IntegrationRoot(), "amazon-fetch-client")),
            new NoOpOperationDiagnostics());

        try
        {
            var status = await client.OpenAsync().AsTask().WaitAsync(LaunchTimeout);
            Assert.Equal(CloakBrowserState.Ready, status.State);

            var response = await client.FetchAsync(AmazonCrawlPolicy.BuildSearchUri("coloring books for adults"))
                .AsTask()
                .WaitAsync(OperationTimeout);

            Assert.True(response.Ok);
            Assert.Contains("text/html", response.ContentType, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(response.Html.Length, 1, AmazonCrawlPolicy.MaximumHtmlBytes);

            var parsed = new AmazonSearchHtmlParser().Parse(response.Html);
            Assert.Equal(AmazonSearchPageDiagnostic.Results, parsed.Diagnostic);
            Assert.NotEmpty(parsed.Candidates);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static async Task<CloakContextHandle> LaunchAsync(string profile)
    {
        var cache = Path.Combine(IntegrationRoot(), "cache");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(cache);
        Environment.SetEnvironmentVariable("CLOAKBROWSER_CACHE_DIR", cache, EnvironmentVariableTarget.Process);

        return await CloakLauncher.LaunchPersistentContextAsync(profile, new LaunchContextOptions
        {
            Headless = true,
            Humanize = false,
            NoViewport = true,
            Locale = "en-US"
        }).WaitAsync(LaunchTimeout);
    }

    private static async Task CloseAsync(CloakContextHandle handle)
    {
        try
        {
            await handle.CloseAsync().WaitAsync(OperationTimeout);
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }

    private static string ProfilePath(string name) => Path.Combine(IntegrationRoot(), "profiles", name);

    private static string IntegrationRoot() => Path.Combine(RepositoryRoot(), "artifacts", "cloakbrowser-integration");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintableBook.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
