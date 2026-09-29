using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Core.Tests.Application.AmazonCrawl;

public sealed class AmazonAsinCrawlWorkerTests
{
    [Fact]
    public async Task Worker_preserves_order_and_selects_unique_asins()
    {
        var page = new FakePageClient(
        [
            Response("one"),
            Response("two"),
            Response("three")
        ]);
        var parser = new QueueParser(
        [
            Parsed(
                ("B000000001", "Coloring book one"),
                ("B000000002", "Coloring book fallback one"),
                ("B000000003", "Coloring book fallback two")),
            Parsed(
                ("B000000001", "Coloring book duplicate one"),
                ("B000000004", "Coloring books two"),
                ("B000000005", "Coloring book fallback three")),
            Parsed(
                ("B000000001", "Coloring book duplicate one"),
                ("B000000004", "Coloring book duplicate two"),
                ("B000000006", "Coloring books three"))
        ]);
        var context = new RecordingContext();
        var worker = new AmazonAsinCrawlWorker(page, parser, new NoDelay());

        var result = Assert.IsType<AmazonAsinCrawlView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(
            AmazonAsinCrawlRequest.Create(["one", "two", "three"]), context, CancellationToken.None));

        Assert.Equal(AmazonAsinCrawlOutcome.Completed, result.Outcome);
        Assert.Equal("B000000001,B000000004,B000000006", result.FinalAsins);
        Assert.Equal(
            [AmazonAsinKeywordStatus.Selected, AmazonAsinKeywordStatus.Selected, AmazonAsinKeywordStatus.Selected],
            result.Rows.Select(row => row.Status));
        Assert.Equal(CloakBrowserState.Ready, page.Status.State);
    }

    [Fact]
    public async Task Worker_retries_transient_fetch_once_then_continues_as_partial()
    {
        var page = new FakePageClient(
        [
            new AmazonSearchPageException("amazon_fetch_timeout", "timeout", retryable: true),
            Response("one"),
            new AmazonSearchPageException("amazon_fetch_failed", "failed", retryable: false)
        ]);
        var parser = new QueueParser([Parsed(("B000000001", "Coloring book"))]);
        var context = new RecordingContext();

        var result = Assert.IsType<AmazonAsinCrawlView>(await ((IBackgroundTaskWorker)new AmazonAsinCrawlWorker(page, parser, new NoDelay())).ExecuteAsync(
            AmazonAsinCrawlRequest.Create(["one", "two"]), context, CancellationToken.None));

        Assert.Equal(AmazonAsinCrawlOutcome.Partial, result.Outcome);
        Assert.Equal("B000000001", result.FinalAsins);
        Assert.Equal(3, page.FetchCount);
        Assert.Equal(AmazonAsinKeywordStatus.Failed, result.Rows[1].Status);
    }

    [Fact]
    public async Task Worker_stops_on_challenge_and_keeps_prior_result()
    {
        var page = new FakePageClient([Response("one"), Response("two")]);
        var parser = new QueueParser(
        [
            Parsed(("B000000001", "Coloring book")),
            new AmazonSearchParseResult(AmazonSearchPageDiagnostic.NeedsAttention, [], "amazon_challenge_detected")
        ]);
        var context = new RecordingContext();

        var result = Assert.IsType<AmazonAsinCrawlView>(await ((IBackgroundTaskWorker)new AmazonAsinCrawlWorker(page, parser, new NoDelay())).ExecuteAsync(
            AmazonAsinCrawlRequest.Create(["one", "two", "three"]), context, CancellationToken.None));

        Assert.Equal(AmazonAsinCrawlOutcome.NeedsAttention, result.Outcome);
        Assert.Equal("B000000001", result.FinalAsins);
        Assert.Equal(AmazonAsinKeywordStatus.NotProcessed, result.Rows[2].Status);
    }

    [Fact]
    public async Task Worker_stops_when_searchbox_is_missing_and_keeps_prior_result()
    {
        var page = new FakePageClient([Response("one"), Response("two")]);
        var parser = new QueueParser(
        [
            Parsed(("B000000001", "Coloring book")),
            new AmazonSearchParseResult(AmazonSearchPageDiagnostic.UnexpectedMarkup, [], "amazon_searchbox_missing")
        ]);
        var context = new RecordingContext();

        var result = Assert.IsType<AmazonAsinCrawlView>(await ((IBackgroundTaskWorker)new AmazonAsinCrawlWorker(page, parser, new NoDelay())).ExecuteAsync(
            AmazonAsinCrawlRequest.Create(["one", "two", "three"]), context, CancellationToken.None));

        Assert.Equal(AmazonAsinCrawlOutcome.Failed, result.Outcome);
        Assert.Equal("amazon_searchbox_missing", result.StopReasonCode);
        Assert.Equal("B000000001", result.FinalAsins);
        Assert.Equal(2, page.FetchCount);
        Assert.Equal(
            [AmazonAsinKeywordStatus.Selected, AmazonAsinKeywordStatus.Failed, AmazonAsinKeywordStatus.NotProcessed],
            result.Rows.Select(row => row.Status));
    }

    [Fact]
    public async Task Worker_stops_when_browser_payload_cannot_be_parsed()
    {
        var page = new FakePageClient(
        [
            Response("one"),
            new AmazonSearchPageException("amazon_fetch_payload_invalid", "invalid payload")
        ]);
        var parser = new QueueParser([Parsed(("B000000001", "Coloring book"))]);
        var context = new RecordingContext();

        var result = Assert.IsType<AmazonAsinCrawlView>(await ((IBackgroundTaskWorker)new AmazonAsinCrawlWorker(page, parser, new NoDelay())).ExecuteAsync(
            AmazonAsinCrawlRequest.Create(["one", "two", "three"]), context, CancellationToken.None));

        Assert.Equal(AmazonAsinCrawlOutcome.Failed, result.Outcome);
        Assert.Equal("amazon_fetch_payload_invalid", result.StopReasonCode);
        Assert.Equal("B000000001", result.FinalAsins);
        Assert.Equal(2, page.FetchCount);
        Assert.Equal(
            [AmazonAsinKeywordStatus.Selected, AmazonAsinKeywordStatus.Failed, AmazonAsinKeywordStatus.NotProcessed],
            result.Rows.Select(row => row.Status));
    }

    [Fact]
    public async Task Worker_publishes_a_terminal_view_when_browser_setup_fails()
    {
        var page = new FakePageClient([])
        {
            OpenException = new AmazonSearchPageException(
                "cloak_browser_license_required",
                "license required",
                needsAttention: true)
        };
        var context = new RecordingContext();

        var result = Assert.IsType<AmazonAsinCrawlView>(await ((IBackgroundTaskWorker)new AmazonAsinCrawlWorker(page, new QueueParser([]), new NoDelay())).ExecuteAsync(
            AmazonAsinCrawlRequest.Create(["one", "two"]), context, CancellationToken.None));

        Assert.Equal(AmazonAsinCrawlOutcome.NeedsAttention, result.Outcome);
        Assert.Equal("cloak_browser_license_required", result.StopReasonCode);
        Assert.All(result.Rows, row => Assert.Equal(AmazonAsinKeywordStatus.NotProcessed, row.Status));
        Assert.Same(result, context.View);
    }

    private static BrowserFetchResponse Response(string marker) => new(200, true, false, "https://www.amazon.com/s", "text/html", marker);

    private static AmazonSearchParseResult Parsed(params (string Asin, string Title)[] candidates) =>
        new(AmazonSearchPageDiagnostic.Results, candidates.Select(item => new AmazonSearchCandidate(item.Asin, item.Title)).ToArray());

    private sealed class NoDelay : IAmazonCrawlDelay
    {
        public ValueTask WaitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class QueueParser(IEnumerable<AmazonSearchParseResult> results) : IAmazonSearchHtmlParser
    {
        private readonly Queue<AmazonSearchParseResult> results = new(results);
        public AmazonSearchParseResult Parse(string html) => results.Dequeue();
    }

    private sealed class FakePageClient(IEnumerable<object> responses) : IAmazonSearchPageClient
    {
        private readonly Queue<object> responses = new(responses);
        public Exception? OpenException { get; init; }
        public CloakBrowserStatus Status { get; private set; } = new(CloakBrowserState.Closed);
        public int FetchCount { get; private set; }
        public ValueTask<CloakBrowserStatus> GetStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Status);
        public ValueTask<CloakBrowserStatus> OpenAsync(CancellationToken cancellationToken = default) => OpenException is null
            ? ValueTask.FromResult(Status = new(CloakBrowserState.Ready))
            : ValueTask.FromException<CloakBrowserStatus>(OpenException);
        public ValueTask<BrowserFetchResponse> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            FetchCount++;
            var next = responses.Dequeue();
            return next is Exception exception
                ? ValueTask.FromException<BrowserFetchResponse>(exception)
                : ValueTask.FromResult((BrowserFetchResponse)next);
        }
    }

    private sealed class RecordingContext : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("amazon-test");
        public object? View { get; private set; }
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class => View = view;
    }
}
