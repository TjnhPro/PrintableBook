using System.Text.Json;
using System.Text.Json.Serialization;
using global::CloakBrowser;
using Microsoft.Playwright;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.Diagnostics;

namespace PrintableBook.Infrastructure.CloakBrowser;

public sealed class CloakBrowserAmazonPageClient : IAmazonSearchPageClient, IAmazonBrowserLifetime, IAsyncDisposable, IDisposable
{
    private const string FetchScript = """
        async ({ url, timeoutMs, maxBytes }) => {
          const controller = new AbortController();
          const timer = setTimeout(() => controller.abort(), timeoutMs);
          try {
            const response = await fetch(url, {
              credentials: 'include',
              redirect: 'follow',
              cache: 'no-store',
              signal: controller.signal
            });
            const contentType = response.headers.get('content-type') || '';
            const declared = Number(response.headers.get('content-length') || 0);
            if (declared > maxBytes) return JSON.stringify({ tooLarge: true, status: response.status, ok: response.ok, redirected: response.redirected, finalUrl: response.url, contentType, html: '' });
            const html = await response.text();
            const bytes = new TextEncoder().encode(html).byteLength;
            return JSON.stringify({ tooLarge: bytes > maxBytes, status: response.status, ok: response.ok, redirected: response.redirected, finalUrl: response.url, contentType, html: bytes > maxBytes ? '' : html });
          } finally {
            clearTimeout(timer);
          }
        }
        """;

    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly CloakBrowserStorageLayout storage;
    private readonly IOperationDiagnostics diagnostics;
    private CloakContextHandle? handle;
    private IPage? page;
    private AmazonMarketplaceProfile? activeProfile;
    private CloakBrowserStatus status = new(CloakBrowserState.Closed);
    private bool disposed;

    public CloakBrowserAmazonPageClient(IOperationDiagnostics diagnostics)
        : this(new CloakBrowserStorageLayout(AppDomain.CurrentDomain.BaseDirectory), diagnostics)
    {
    }

    internal CloakBrowserAmazonPageClient(CloakBrowserStorageLayout storage, IOperationDiagnostics diagnostics)
    {
        this.storage = storage;
        this.diagnostics = diagnostics;
    }

    public async ValueTask<CloakBrowserStatus> GetStatusAsync(AmazonMarketplaceProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            var observed = status.State == CloakBrowserState.Ready && (page is null || page.IsClosed)
                ? new CloakBrowserStatus(CloakBrowserState.Closed, "cloak_browser_closed")
                : status;
            return Describe(observed, profile, page is { IsClosed: false } ? activeProfile : null);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public ValueTask<CloakBrowserStatus> OpenAsync(AmazonMarketplaceProfile profile, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(profile, fresh: false, cancellationToken);

    public ValueTask<CloakBrowserStatus> OpenFreshAsync(AmazonMarketplaceProfile profile, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(profile, fresh: true, cancellationToken);

    private async ValueTask<CloakBrowserStatus> OpenCoreAsync(
        AmazonMarketplaceProfile profile,
        bool fresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!fresh && status.State == CloakBrowserState.Ready && page is { IsClosed: false } &&
                string.Equals(activeProfile?.MarketCode, profile.MarketCode, StringComparison.Ordinal))
            {
                await page.BringToFrontAsync();
                return Describe(status, profile, activeProfile);
            }

            try
            {
                await CloseContextCoreAsync();
            }
            catch (Exception exception)
            {
                throw MapCloseException(exception);
            }
            status = new(CloakBrowserState.Checking);
            storage.EnsureWritable(profile);
            Environment.SetEnvironmentVariable("CLOAKBROWSER_CACHE_DIR", storage.Cache, EnvironmentVariableTarget.Process);
            status = new(CloakBrowserState.Opening);
            diagnostics.Record("amazon.browser.opening", profile.MarketCode, $"{profile.Domain}; CloakBrowser 0.5.11");

            handle = await CloakLauncher.LaunchPersistentContextAsync(storage.ProfileFor(profile), new LaunchContextOptions
            {
                Headless = false,
                Humanize = false,
                NoViewport = true,
                Locale = profile.Locale
            }).WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
            activeProfile = profile;
            page = handle.Context.Pages.FirstOrDefault() ?? await handle.NewPageAsync();
            status = new(CloakBrowserState.WarmingUp);
            await page.GotoAsync(new Uri(profile.MarketplaceUri, "gp/cart/view.html?ref_=nav_cart").AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 45_000
            }).WaitAsync(cancellationToken);

            if (!AmazonCrawlPolicy.IsAllowedUri(profile, TryUri(page.Url)))
            {
                throw new AmazonSearchPageException("amazon_origin_not_ready", "Amazon did not open on the expected origin.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            status = new(CloakBrowserState.Ready);
            diagnostics.Record("amazon.browser.ready", profile.MarketCode, profile.Domain);
            return Describe(status, profile, activeProfile);
        }
        catch (OperationCanceledException)
        {
            try
            {
                await CloseContextCoreAsync();
            }
            catch (Exception exception)
            {
                diagnostics.Record("amazon.browser.close.warning", profile.MarketCode, exception.Message);
            }
            status = new(CloakBrowserState.Closed);
            throw;
        }
        catch (AmazonCrawlStorageException exception)
        {
            status = new(CloakBrowserState.Error, exception.Code);
            throw new AmazonSearchPageException(exception.Code, exception.Message, innerException: exception);
        }
        catch (AmazonSearchPageException exception)
        {
            status = new(exception.NeedsAttention ? CloakBrowserState.NeedsAttention : CloakBrowserState.Error, exception.Code);
            throw;
        }
        catch (Exception exception)
        {
            var mapped = MapLaunchException(exception);
            status = new(mapped.NeedsAttention ? CloakBrowserState.NeedsAttention : CloakBrowserState.Error, mapped.Code);
            throw mapped;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async ValueTask<BrowserFetchResponse> FetchAsync(AmazonMarketplaceProfile profile, Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!AmazonCrawlPolicy.IsAllowedUri(profile, uri))
        {
            throw new AmazonSearchPageException("amazon_url_not_allowed", $"Only {profile.Domain} search pages are allowed.");
        }

        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (status.State != CloakBrowserState.Ready || page is null || page.IsClosed)
            {
                throw new AmazonSearchPageException("amazon_origin_not_ready", "Open the Amazon browser before searching.");
            }
            if (!string.Equals(activeProfile?.MarketCode, profile.MarketCode, StringComparison.Ordinal) ||
                !AmazonCrawlPolicy.IsAllowedUri(profile, TryUri(page.Url)))
            {
                throw new AmazonSearchPageException("amazon_origin_not_ready", $"Return the browser to {profile.Domain} before searching.");
            }

            string payloadJson;
            try
            {
                payloadJson = await page.EvaluateAsync<string>(FetchScript, new
                {
                    url = uri.AbsoluteUri,
                    timeoutMs = (int)AmazonCrawlPolicy.FetchTimeout.TotalMilliseconds,
                    maxBytes = AmazonCrawlPolicy.MaximumHtmlBytes
                }).WaitAsync(AmazonCrawlPolicy.FetchTimeout + TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException exception)
            {
                throw new AmazonSearchPageException("amazon_fetch_timeout", "Amazon search timed out. Try the crawl again.", retryable: true, innerException: exception);
            }
            catch (PlaywrightException exception) when (exception.Message.Contains("AbortError", StringComparison.OrdinalIgnoreCase))
            {
                throw new AmazonSearchPageException("amazon_fetch_timeout", "Amazon search timed out. Try the crawl again.", retryable: true, innerException: exception);
            }

            var payload = DeserializeFetchPayload(payloadJson);

            if (payload.TooLarge)
            {
                throw new AmazonSearchPageException("amazon_response_too_large", "Amazon returned more data than the safe response limit.");
            }
            if (!AmazonCrawlPolicy.IsAllowedUri(profile, TryUri(payload.FinalUrl)))
            {
                throw new AmazonSearchPageException("amazon_redirect_not_allowed", "Amazon redirected the search outside the allowed origin.");
            }
            if (payload.Status is 403 or 429 or 503)
            {
                throw new AmazonSearchPageException("amazon_rate_limited", "Amazon needs attention in the browser before crawling can continue.", needsAttention: true);
            }
            if (!payload.ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                throw new AmazonSearchPageException("amazon_response_not_html", "Amazon did not return an HTML search page.");
            }
            if (!payload.Ok)
            {
                var retryable = payload.Status is 408 or 500 or 502 or 504;
                throw new AmazonSearchPageException("amazon_fetch_failed", $"Amazon search returned HTTP {payload.Status}.", retryable);
            }

            return new(payload.Status, payload.Ok, payload.Redirected, payload.FinalUrl, payload.ContentType, payload.Html);
        }
        catch (AmazonSearchPageException)
        {
            throw;
        }
        catch (PlaywrightException exception)
        {
            if (page is null || page.IsClosed)
            {
                status = new(CloakBrowserState.Error, "cloak_browser_closed");
                throw new AmazonSearchPageException("cloak_browser_closed", "The Amazon browser closed or disconnected. Open it and try again.", innerException: exception);
            }
            throw new AmazonSearchPageException("amazon_fetch_failed", "Amazon search could not be fetched. Check the browser connection and try again.", retryable: true, innerException: exception);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            var closingMarketCode = activeProfile?.MarketCode;
            try
            {
                await CloseContextCoreAsync();
                status = new(CloakBrowserState.Closed);
            }
            catch (Exception exception)
            {
                var mapped = MapCloseException(exception);
                status = new(CloakBrowserState.Error, mapped.Code);
                diagnostics.Record("amazon.browser.close.warning", closingMarketCode, exception.Message);
                throw mapped;
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public ValueTask ShutdownAsync(CancellationToken cancellationToken = default) => CloseAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await ShutdownAsync();
        disposed = true;
        lifecycle.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        try { ShutdownAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception) { }
        disposed = true;
        lifecycle.Dispose();
    }

    private async ValueTask CloseContextCoreAsync()
    {
        var current = handle;
        page = null;
        handle = null;
        activeProfile = null;
        if (current is null) return;

        Exception? failure = null;
        try
        {
            await current.CloseAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await current.DisposeAsync();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null) throw failure;
    }

    private static CloakBrowserStatus Describe(
        CloakBrowserStatus value,
        AmazonMarketplaceProfile target,
        AmazonMarketplaceProfile? active) =>
        value with
        {
            TargetMarketCode = target.MarketCode,
            TargetMarketName = target.MarketName,
            TargetDomain = target.Domain,
            ActiveMarketCode = active?.MarketCode
        };

    private static Uri? TryUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    internal FetchPayload DeserializeFetchPayload(string payloadJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                throw new JsonException("The browser returned an empty fetch payload.");
            }

            var payload = JsonSerializer.Deserialize<FetchPayload>(payloadJson)
                ?? throw new JsonException("The browser returned a null fetch payload.");
            if (payload.Status is < 100 or > 599)
            {
                throw new JsonException($"The browser returned an invalid HTTP status '{payload.Status}'.");
            }
            if (payload.FinalUrl is null)
            {
                throw new JsonException("The browser fetch payload is missing 'finalUrl'.");
            }
            if (payload.ContentType is null)
            {
                throw new JsonException("The browser fetch payload is missing 'contentType'.");
            }
            if (payload.Html is null)
            {
                throw new JsonException("The browser fetch payload is missing 'html'.");
            }

            return payload;
        }
        catch (JsonException exception)
        {
            diagnostics.Record("amazon.fetch.payload.invalid", detail: exception.Message);
            throw new AmazonSearchPageException(
                "amazon_fetch_payload_invalid",
                $"Amazon fetch payload could not be parsed: {exception.Message}",
                innerException: exception);
        }
    }

    private static AmazonSearchPageException MapLaunchException(Exception exception)
    {
        var text = exception.Message;
        if (text.Contains("license", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("invalid", StringComparison.OrdinalIgnoreCase) || text.Contains("expired", StringComparison.OrdinalIgnoreCase)))
        {
            return new("cloak_browser_license_invalid", "The CloakBrowser access key or license is invalid or expired.", needsAttention: true, innerException: exception);
        }
        if (text.Contains("license", StringComparison.OrdinalIgnoreCase))
        {
            return new("cloak_browser_license_required", "CloakBrowser needs a valid license or free access key.", needsAttention: true, innerException: exception);
        }
        if (text.Contains("lock", StringComparison.OrdinalIgnoreCase) || text.Contains("user data directory is already in use", StringComparison.OrdinalIgnoreCase))
        {
            return new("cloak_browser_profile_locked", "The Amazon browser profile is already in use. Close the other browser or app instance and retry.", innerException: exception);
        }
        if (text.Contains("download", StringComparison.OrdinalIgnoreCase) || text.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return new("cloak_browser_download_failed", "CloakBrowser components could not be downloaded. Check network, disk space, and antivirus, then retry.", innerException: exception);
        }
        return new("cloak_browser_launch_failed", "CloakBrowser could not be opened. Check the browser setup and retry.", innerException: exception);
    }

    private static AmazonSearchPageException MapCloseException(Exception exception) =>
        exception as AmazonSearchPageException ?? new(
            "cloak_browser_close_failed",
            "The Amazon browser context could not be closed cleanly.",
            innerException: exception);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    internal sealed record FetchPayload(
        [property: JsonPropertyName("tooLarge")] bool TooLarge,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("redirected")] bool Redirected,
        [property: JsonPropertyName("finalUrl")] string FinalUrl,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("html")] string Html);
}
