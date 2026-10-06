using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Desktop.Bridge;
using PrintableBook.Desktop.Loading;

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
        var router = new WebViewBridgeRouter(CreateCoordinator("de"), amazonSearchPageClient: browser);

        var status = await router.HandleAsync("""{"version":1,"id":"status","command":"amazon.browser.status","payload":{"bookId":"Book One"}}""");
        Assert.True(status.Ok);
        Assert.Equal(0, browser.OpenCount);
        Assert.Equal("de", browser.StatusProfile?.MarketCode);

        var open = await router.HandleAsync("""{"version":1,"id":"open","command":"amazon.browser.open","payload":{"bookId":"Book One"}}""");

        Assert.True(open.Ok);
        Assert.Equal("amazon.browser.status", open.Command);
        Assert.Equal(1, browser.OpenCount);
        Assert.Equal("de", browser.OpenProfile?.MarketCode);
    }

    [Fact]
    public async Task Manual_browser_open_is_rejected_while_a_crawl_is_active()
    {
        var browser = new StubBrowser();
        var session = new StubSession { Active = true };
        var router = new WebViewBridgeRouter(CreateCoordinator("en"), amazonAsinCrawlSessionService: session, amazonSearchPageClient: browser);

        var response = await router.HandleAsync("""{"version":1,"id":"open","command":"amazon.browser.open","payload":{"bookId":"Book One"}}""");

        Assert.Equal("amazon_asin_crawl_active", response.Error);
        Assert.Equal(0, browser.OpenCount);
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
        public AmazonMarketplaceProfile? StatusProfile { get; private set; }
        public AmazonMarketplaceProfile? OpenProfile { get; private set; }
        public ValueTask<CloakBrowserStatus> GetStatusAsync(AmazonMarketplaceProfile profile, CancellationToken cancellationToken = default)
        {
            StatusProfile = profile;
            return ValueTask.FromResult(new CloakBrowserStatus(CloakBrowserState.Closed));
        }
        public ValueTask<CloakBrowserStatus> OpenAsync(AmazonMarketplaceProfile profile, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            OpenProfile = profile;
            return ValueTask.FromResult(new CloakBrowserStatus(CloakBrowserState.Ready));
        }
        public ValueTask<CloakBrowserStatus> OpenFreshAsync(AmazonMarketplaceProfile profile, CancellationToken cancellationToken = default) => OpenAsync(profile, cancellationToken);
        public ValueTask CloseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<BrowserFetchResponse> FetchAsync(AmazonMarketplaceProfile profile, Uri uri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubSession : IAmazonAsinCrawlSessionService
    {
        public bool ValidationError { get; init; }
        public bool Active { get; init; }
        public string? BookId { get; private set; }
        public IReadOnlyList<string>? Keywords { get; private set; }

        public ValueTask<AmazonAsinCrawlSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot(bookId));

        public ValueTask<AmazonAsinCrawlSessionSnapshot> StartAsync(string bookId, IReadOnlyList<string> keywords, AmazonMarketplaceProfile profile, CancellationToken cancellationToken = default)
        {
            if (ValidationError) throw new AmazonCrawlValidationException("amazon_keywords_required", "Required");
            BookId = bookId;
            Keywords = keywords;
            return ValueTask.FromResult(Snapshot(bookId));
        }

        public ValueTask<AmazonAsinCrawlSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot(bookId));
        public ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        private AmazonAsinCrawlSessionSnapshot Snapshot(string bookId) => new(null, bookId, Active, false, null);
    }

    private static ApplicationLoadCoordinator CreateCoordinator(string languageCode) =>
        new(new SnapshotTaskManager(CreateSnapshot(languageCode)));

    private static ApplicationSnapshot CreateSnapshot(string languageCode)
    {
        var id = new BookId("Book One");
        var book = new DiscoveredBook(
            "Book One",
            id,
            new DirectoryReference("sources/Book One"),
            new BookWorkspace(id, new DirectoryReference("workspace"), new DirectoryReference("processed"), new DirectoryReference("temporary")));
        return new ApplicationSnapshot(
            new ApplicationDiscovery(
                new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")),
                [],
                [book]),
            GlobalSettings.Default,
            [new BookDesktopSummary(id, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 0, LanguageCode: languageCode)],
            DateTimeOffset.UnixEpoch);
    }

    private sealed class SnapshotTaskManager(ApplicationSnapshot snapshot) : IBackgroundTaskManager
    {
        private static readonly BackgroundTaskId TaskId = new("snapshot");
        private static readonly BackgroundTaskSnapshot Task = new(
            TaskId,
            BackgroundTaskKind.LibraryRefresh,
            BackgroundTaskState.Completed,
            "library",
            "Library",
            null,
            null,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null);

        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Task);
        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>([Task]);
        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
        {
            result = snapshot is TResult typed ? typed : default;
            return result is not null;
        }
        public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? view) where TView : class
        {
            view = null;
            return false;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintableBook.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
