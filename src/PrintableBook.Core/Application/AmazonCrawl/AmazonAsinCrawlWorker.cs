using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Core.Application.AmazonCrawl;

public interface IAmazonCrawlDelay
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

public sealed class AmazonCrawlJitterDelay : IAmazonCrawlDelay
{
    public ValueTask WaitAsync(CancellationToken cancellationToken) =>
        new(Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1_000, 2_001)), cancellationToken));
}

public sealed class AmazonAsinCrawlWorker(
    IAmazonSearchPageClient pageClient,
    IAmazonSearchHtmlParser parser,
    IAmazonCrawlDelay delay) : BackgroundTaskWorker<AmazonAsinCrawlRequest, AmazonAsinCrawlView>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.AmazonAsinCrawl;

    protected override async ValueTask<AmazonAsinCrawlView> ExecuteTypedAsync(
        AmazonAsinCrawlRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        var rows = AmazonAsinCrawlView.Pending(request).Rows.ToArray();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AmazonCrawlPolicy.CrawlTimeout);
        var token = timeout.Token;

        try
        {
            context.Report("amazon.browser.ensure", 0, request.Keywords.Count, "Preparing Amazon browser");
            await pageClient.OpenAsync(token);
            Publish(AmazonAsinCrawlOutcome.Running);

            for (var index = 0; index < request.Keywords.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                rows[index] = rows[index] with { Status = AmazonAsinKeywordStatus.Searching, ReasonCode = null };
                context.Report("amazon.search", index, request.Keywords.Count, "Searching Amazon");
                Publish(AmazonAsinCrawlOutcome.Running);

                try
                {
                    var response = await FetchWithRetryAsync(AmazonCrawlPolicy.BuildSearchUri(request.Keywords[index]), token);
                    var parsed = parser.Parse(response.Html);
                    rows[index] = AmazonAsinSelection.Select(index, request.Keywords[index], parsed, selected);

                    if (parsed.Diagnostic is AmazonSearchPageDiagnostic.NeedsAttention or AmazonSearchPageDiagnostic.UnexpectedMarkup)
                    {
                        MarkRemainingNotProcessed(index + 1);
                        var outcome = parsed.Diagnostic == AmazonSearchPageDiagnostic.NeedsAttention
                            ? AmazonAsinCrawlOutcome.NeedsAttention
                            : AmazonAsinCrawlOutcome.Failed;
                        return Publish(outcome, parsed.ReasonCode);
                    }
                }
                catch (AmazonSearchPageException exception)
                {
                    rows[index] = rows[index] with { Status = AmazonAsinKeywordStatus.Failed, ReasonCode = exception.Code };
                    if (exception.NeedsAttention || IsStopError(exception.Code))
                    {
                        MarkRemainingNotProcessed(index + 1);
                        return Publish(exception.NeedsAttention ? AmazonAsinCrawlOutcome.NeedsAttention : AmazonAsinCrawlOutcome.Failed, exception.Code);
                    }
                }

                Publish(AmazonAsinCrawlOutcome.Running);
                if (index + 1 < request.Keywords.Count) await delay.WaitAsync(token);
            }

            return Publish(rows.Any(row => row.Status == AmazonAsinKeywordStatus.Failed)
                ? AmazonAsinCrawlOutcome.Partial
                : AmazonAsinCrawlOutcome.Completed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var searching = Array.FindIndex(rows, row => row.Status == AmazonAsinKeywordStatus.Searching);
            if (searching >= 0) rows[searching] = rows[searching] with { Status = AmazonAsinKeywordStatus.Cancelled, ReasonCode = "amazon_crawl_cancelled" };
            MarkRemainingNotProcessed(0);
            Publish(AmazonAsinCrawlOutcome.Cancelled, timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "amazon_crawl_timeout" : "amazon_crawl_cancelled");
            throw;
        }
        catch (AmazonSearchPageException exception)
        {
            MarkRemainingNotProcessed(0);
            return Publish(
                exception.NeedsAttention ? AmazonAsinCrawlOutcome.NeedsAttention : AmazonAsinCrawlOutcome.Failed,
                exception.Code);
        }

        async ValueTask<BrowserFetchResponse> FetchWithRetryAsync(Uri uri, CancellationToken tokenValue)
        {
            for (var attempt = 0; ; attempt++)
            {
                try { return await pageClient.FetchAsync(uri, tokenValue); }
                catch (AmazonSearchPageException exception) when (exception.Retryable && attempt == 0)
                {
                    await delay.WaitAsync(tokenValue);
                }
            }
        }

        void MarkRemainingNotProcessed(int start)
        {
            for (var index = start; index < rows.Length; index++)
            {
                if (rows[index].Status == AmazonAsinKeywordStatus.Pending)
                {
                    rows[index] = rows[index] with { Status = AmazonAsinKeywordStatus.NotProcessed, ReasonCode = "amazon_not_processed" };
                }
            }
        }

        AmazonAsinCrawlView Publish(AmazonAsinCrawlOutcome outcome, string? stopReason = null)
        {
            var completed = rows.Count(row => row.Status is not AmazonAsinKeywordStatus.Pending and not AmazonAsinKeywordStatus.Searching and not AmazonAsinKeywordStatus.NotProcessed);
            var final = string.Join(',', rows.Where(row => row.Status == AmazonAsinKeywordStatus.Selected).Select(row => row.Asin));
            var view = new AmazonAsinCrawlView(outcome, rows.ToArray(), completed, rows.Length, final, request.RequestFingerprint, stopReason);
            context.SetView(view);
            return view;
        }
    }

    private static bool IsStopError(string code) => code is
        "amazon_markup_unsupported" or
        "amazon_response_too_large" or
        "amazon_redirect_not_allowed" or
        "amazon_response_not_html" or
        "amazon_origin_not_ready" or
        "cloak_browser_closed" or
        "cloak_browser_profile_locked" or
        "cloak_browser_launch_failed" or
        "cloak_browser_download_failed" or
        "cloak_browser_license_invalid" or
        "cloak_browser_license_required";
}
