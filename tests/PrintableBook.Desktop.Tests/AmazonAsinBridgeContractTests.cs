using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Desktop.Bridge;

namespace PrintableBook.Desktop.Tests;

public sealed class AmazonAsinBridgeContractTests
{
    [Fact]
    public void Desktop_publish_keeps_the_playwright_driver_outside_the_single_file_bundle()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "PrintableBook.Desktop", "PrintableBook.Desktop.csproj"));

        Assert.Contains("ExcludePlaywrightDriverFromSingleFile", project, StringComparison.Ordinal);
        Assert.Contains("ExcludeFromSingleFile=\"true\"", project, StringComparison.Ordinal);
        Assert.Contains(".playwright\\", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browser_status_and_open_use_the_typed_browser_boundary()
    {
        var browser = new StubBrowser();
        var router = new WebViewBridgeRouter(amazonSearchPageClient: browser);

        var status = await router.HandleAsync("""{"version":1,"id":"status","command":"amazon.browser.status"}""");
        var open = await router.HandleAsync("""{"version":1,"id":"open","command":"amazon.browser.open"}""");

        Assert.True(status.Ok);
        Assert.True(open.Ok);
        Assert.Equal("amazon.browser.status", open.Command);
        Assert.Equal(1, browser.OpenCount);
    }

    [Fact]
    public async Task Crawl_start_rejects_legacy_raw_keyword_payloads()
    {
        var session = new StubSession();
        var router = new WebViewBridgeRouter(amazonAsinCrawlSessionService: session);

        var invalid = await router.HandleAsync("""{"version":1,"id":"bad","command":"book.keywords.asin-crawl.start","payload":{"bookId":"book"}}""");
        var valid = await router.HandleAsync("""{"version":1,"id":"ok","command":"book.keywords.asin-crawl.start","payload":{"bookId":"book","keywords":["cozy cats","adult coloring"]}}""");

        Assert.Equal("keyword_preview_required", invalid.Error);
        Assert.Equal("keyword_preview_required", valid.Error);
        Assert.Null(session.BookId);
        Assert.Null(session.Keywords);
    }

    [Fact]
    public async Task Crawl_without_preview_service_returns_stable_safe_error_code()
    {
        var router = new WebViewBridgeRouter(amazonAsinCrawlSessionService: new StubSession { ValidationError = true });

        var response = await router.HandleAsync("""{"version":1,"id":"empty","command":"book.keywords.asin-crawl.start","payload":{"bookId":"book","keywords":[]}}""");

        Assert.False(response.Ok);
        Assert.Equal("keyword_preview_required", response.Error);
    }

    private sealed class StubBrowser : IAmazonSearchPageClient
    {
        public int OpenCount { get; private set; }
        public ValueTask<CloakBrowserStatus> GetStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new CloakBrowserStatus(CloakBrowserState.Closed));
        public ValueTask<CloakBrowserStatus> OpenAsync(CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return ValueTask.FromResult(new CloakBrowserStatus(CloakBrowserState.Ready));
        }
        public ValueTask<BrowserFetchResponse> FetchAsync(Uri uri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubSession : IAmazonAsinCrawlSessionService
    {
        public bool ValidationError { get; init; }
        public string? BookId { get; private set; }
        public IReadOnlyList<string>? Keywords { get; private set; }

        public ValueTask<AmazonAsinCrawlSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot(bookId));

        public ValueTask<AmazonAsinCrawlSessionSnapshot> StartAsync(string bookId, IReadOnlyList<string> keywords, CancellationToken cancellationToken = default)
        {
            if (ValidationError) throw new AmazonCrawlValidationException("amazon_keywords_required", "Required");
            BookId = bookId;
            Keywords = keywords;
            return ValueTask.FromResult(Snapshot(bookId));
        }

        public ValueTask<AmazonAsinCrawlSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot(bookId));
        public ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        private static AmazonAsinCrawlSessionSnapshot Snapshot(string bookId) => new(null, bookId, false, false, null);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintableBook.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
