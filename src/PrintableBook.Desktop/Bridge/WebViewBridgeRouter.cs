using System.Text.Json;
using System.ComponentModel;
using System.IO;
using System.Collections.Concurrent;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Desktop.Loading;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Desktop.Diagnostics;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Books;
using PrintableBook.Desktop.BackgroundTasks;
using PrintableBook.Desktop.Updates;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Desktop.Bridge;

/// <summary>
/// Parses and routes the narrow, versioned messages accepted from the WebView.
/// </summary>
internal sealed class WebViewBridgeRouter(
    ApplicationLoadCoordinator? applicationLoadCoordinator = null,
    IGlobalSettingsStore? settingsStore = null,
    IProcessSessionService? processSessionService = null,
    IBookCoverSelectionService? coverSelectionService = null,
    IInteriorFrameModeService? interiorFrameModeService = null,
    IBookInteriorSettingsService? bookInteriorSettingsService = null,
    ILocalOutputActionService? outputActionService = null,
    IOperationDiagnostics? diagnostics = null,
    UiDiagnosticsService? uiDiagnosticsService = null,
    IBackgroundTaskManager? backgroundTaskManager = null,
    ProcessingMutationGate? processingMutationGate = null,
    IBrandValidationService? brandValidationService = null,
    IBrandTemplateCopyService? brandTemplateCopyService = null,
    IDesktopUpdateCoordinator? updateCoordinator = null,
    IProductionFilePicker? productionFilePicker = null,
    IProductionAssetImportService? productionAssetImportService = null,
    IBookCatalogMetadataService? bookCatalogMetadataService = null,
    IBookKeywordPreviewService? bookKeywordPreviewService = null,
    IAmazonAsinCrawlSessionService? amazonAsinCrawlSessionService = null,
    IAmazonSearchPageClient? amazonSearchPageClient = null,
    IS3StorageService? s3StorageService = null,
    IProductionPdfNameSuggestionService? productionPdfNameSuggestionService = null,
    IInteriorShuffleService? interiorShuffleService = null,
    IBrandCloneService? brandCloneService = null,
    IBookCloneService? bookCloneService = null)
{
    private readonly IOperationDiagnostics diagnostics = diagnostics ?? new NoOpOperationDiagnostics();
    private readonly ProcessingMutationGate processingMutationGate = processingMutationGate ?? new ProcessingMutationGate();
    private readonly ConcurrentDictionary<string, KeywordCrawlSource> crawlSources = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const int Version = 1;

    public BridgeResponse Handle(string? json)
    {
        return TryParseRequest(json, out var request)
            ? RouteSynchronous(request)
            : BridgeResponse.InvalidRequest();
    }

    public async ValueTask<BridgeResponse> HandleAsync(string? json, CancellationToken cancellationToken = default)
    {
        if (!TryParseRequest(json, out var request)) return BridgeResponse.InvalidRequest();

        try
        {
            if (request.Command == "updates.getState")
            {
                return updateCoordinator is null ? BridgeResponse.UnsupportedCommand(request.Id) : BridgeResponse.Succeeded(request.Id, "updates.state", UpdateBridgeSnapshot.From(updateCoordinator.GetState()));
            }
            if (request.Command == "updates.check")
            {
                if (updateCoordinator is null) return BridgeResponse.UnsupportedCommand(request.Id);
                var trigger = UpdateCheckTrigger.Manual;
                if (request.Payload is { } updatePayload && updatePayload.TryGetProperty("trigger", out var triggerElement))
                {
                    var triggerValue = triggerElement.ValueKind == JsonValueKind.String ? triggerElement.GetString() : null;
                    if (triggerValue is null ||
                        (!string.Equals(triggerValue, "automatic", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(triggerValue, "manual", StringComparison.OrdinalIgnoreCase)) ||
                        !Enum.TryParse<UpdateCheckTrigger>(triggerValue, true, out trigger))
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "invalid_update_check_trigger");
                    }
                }
                return BridgeResponse.Succeeded(request.Id, "updates.state", UpdateBridgeSnapshot.From(await updateCoordinator.CheckAsync(trigger, cancellationToken)));
            }
            if (request.Command == "updates.download")
            {
                return updateCoordinator is null ? BridgeResponse.UnsupportedCommand(request.Id) : BridgeResponse.Succeeded(request.Id, "updates.state", UpdateBridgeSnapshot.From(await updateCoordinator.DownloadAsync(cancellationToken)));
            }
            if (request.Command == "updates.install")
            {
                return updateCoordinator is null ? BridgeResponse.UnsupportedCommand(request.Id) : BridgeResponse.Succeeded(request.Id, "updates.state", UpdateBridgeSnapshot.From(await updateCoordinator.InstallAsync(cancellationToken)));
            }
            using var operation = diagnostics.Begin($"bridge.{request.Command}");
            var response = RouteSynchronous(request);
            if (response.Error is not null || response.Command is not null) return response;
            if (request.Command == "diagnostics.get")
            {
                return uiDiagnosticsService is null
                    ? BridgeResponse.UnsupportedCommand(request.Id)
                    : BridgeResponse.Succeeded(request.Id, "diagnostics.snapshot", uiDiagnosticsService.Snapshot());
            }
            if (request.Command == "amazon.browser.status")
            {
                if (amazonSearchPageClient is null) return BridgeResponse.UnsupportedCommand(request.Id);
                return BridgeResponse.Succeeded(request.Id, "amazon.browser.status", await amazonSearchPageClient.GetStatusAsync(cancellationToken));
            }
            if (request.Command == "amazon.browser.open")
            {
                if (amazonSearchPageClient is null) return BridgeResponse.UnsupportedCommand(request.Id);
                try
                {
                    return BridgeResponse.Succeeded(request.Id, "amazon.browser.status", await amazonSearchPageClient.OpenAsync(cancellationToken));
                }
                catch (AmazonSearchPageException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                }
            }
            if (request.Command is "book.keywords.asin-crawl.start" or "book.keywords.asin-crawl.get" or "book.keywords.asin-crawl.cancel")
            {
                if (amazonAsinCrawlSessionService is null || request.Payload is not { } crawlPayload ||
                    !TryGetRequiredString(crawlPayload, "bookId", out var crawlBookId))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_amazon_asin_crawl");
                }

                try
                {
                    AmazonAsinCrawlSessionSnapshot snapshot;
                    if (request.Command == "book.keywords.asin-crawl.start")
                    {
                        if (bookKeywordPreviewService is null || applicationLoadCoordinator is null)
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "keyword_preview_required");
                        }
                        if (!TryGetOptionalString(crawlPayload, "previewReceipt", out var previewReceipt) ||
                            !TryGetOptionalString(crawlPayload, "savedBuildId", out var savedBuildId) ||
                            string.IsNullOrWhiteSpace(previewReceipt) == string.IsNullOrWhiteSpace(savedBuildId))
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_amazon_asin_crawl");
                        }
                        var current = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                        var book = current?.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, crawlBookId, StringComparison.Ordinal));
                        if (book is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                        var trustedSource = await bookKeywordPreviewService.ResolveCrawlSourceAsync(book, previewReceipt, savedBuildId, cancellationToken);
                        snapshot = await amazonAsinCrawlSessionService.StartAsync(crawlBookId, trustedSource.Keywords, cancellationToken);
                        crawlSources[crawlBookId] = trustedSource;
                    }
                    else if (request.Command == "book.keywords.asin-crawl.cancel")
                    {
                        snapshot = await amazonAsinCrawlSessionService.CancelAsync(crawlBookId, cancellationToken);
                    }
                    else
                    {
                        snapshot = await amazonAsinCrawlSessionService.GetAsync(crawlBookId, cancellationToken);
                    }
                    if (crawlSources.TryGetValue(crawlBookId, out var source))
                    {
                        snapshot = snapshot with { SourceFingerprint = source.SourceFingerprint, ReceiptDigest = source.ReceiptDigest };
                    }
                    return BridgeResponse.Succeeded(request.Id, "book.keywords.asin-crawl", snapshot);
                }
                catch (AmazonCrawlValidationException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                }
                catch (KeywordPreviewException exception)
                {
                    return KeywordPreviewFailure(request.Id, exception);
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.AmazonAsinCrawl)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "amazon_asin_crawl_active");
                }
            }
            if (request.Command == "app.refresh")
            {
                if (applicationLoadCoordinator is null) return BridgeResponse.UnsupportedCommand(request.Id);
                try
                {
                    return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.CacheCleanup)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "cache_cleanup_active");
                }
            }
            if (request.Command == "app.refresh.result")
            {
                if (applicationLoadCoordinator is null || request.Payload is not { } resultPayload ||
                    !resultPayload.TryGetProperty("taskId", out var taskIdElement) ||
                    !TryParseTaskId(taskIdElement, out var taskId)) return new BridgeResponse(Version, request.Id, false, null, "invalid_task_id");
                var task = await applicationLoadCoordinator.GetTaskAsync(taskId, cancellationToken);
                if (task is null || task.Kind != BackgroundTaskKind.LibraryRefresh || task.State != BackgroundTaskState.Completed)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "task_not_completed");
                }
                if (!applicationLoadCoordinator.TryGetResult(taskId, out var completedSnapshot) || completedSnapshot is null)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "app_refresh_failed");
                }
                return BridgeResponse.Succeeded(request.Id, "app.snapshot", completedSnapshot);
            }
            if (request.Command == "brand.clone")
            {
                if (applicationLoadCoordinator is null || brandCloneService is null || request.Payload is not { } clonePayload ||
                    !TryGetRequiredString(clonePayload, "brandName", out var requestedBrandName) ||
                    !TryGetRequiredString(clonePayload, "languageCode", out var requestedLanguageCode))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_clone");
                }

                var brandName = requestedBrandName.Trim();
                if (!SupportedLanguageCatalog.TryGet(requestedLanguageCode, out var language))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "brand_clone_language_invalid");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken))
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                    }

                    ApplicationSnapshot snapshot;
                    try
                    {
                        snapshot = await applicationLoadCoordinator.GetFreshAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                    }

                    var sourceBrand = snapshot.Discovery.Brands.FirstOrDefault(item => string.Equals(item.Name, brandName, StringComparison.Ordinal));
                    if (sourceBrand is null)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "brand_clone_source_not_found");
                    }

                    BrandCloneResult result;
                    try
                    {
                        result = await brandCloneService.CloneAsync(snapshot.Discovery.Paths, sourceBrand, language, cancellationToken);
                    }
                    catch (BrandCloneException exception)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "brand_clone_failed");
                    }

                    BackgroundTaskBridgeSnapshot? refreshTask = null;
                    string? refreshWarning = null;
                    try
                    {
                        refreshTask = BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(CancellationToken.None));
                    }
                    catch
                    {
                        refreshWarning = "Library refresh could not start.";
                    }

                    return BridgeResponse.Succeeded(request.Id, "brand.clone.completed", new
                    {
                        sourceBrandName = result.SourceBrand.Name,
                        languageCode = result.Language.Code,
                        languageName = result.Language.Name,
                        destinationBrandName = result.DestinationBrand.Name,
                        refreshTask,
                        refreshWarning
                    });
                }
            }
            if (request.Command == "book.clone")
            {
                if (applicationLoadCoordinator is null || bookCloneService is null || request.Payload is not { } clonePayload ||
                    !TryGetRequiredString(clonePayload, "bookId", out var requestedBookId) ||
                    !TryGetRequiredString(clonePayload, "languageCode", out var requestedLanguageCode))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_book_clone");
                }

                var bookId = requestedBookId.Trim();
                if (!SupportedLanguageCatalog.TryGet(requestedLanguageCode, out var language))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "book_clone_language_invalid");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken))
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                    }
                    var writerError = await GetActiveStateWriterErrorAsync(cancellationToken);
                    if (writerError is not null)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, writerError);
                    }

                    ApplicationSnapshot snapshot;
                    try
                    {
                        snapshot = await applicationLoadCoordinator.GetFreshAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                    }

                    var sourceBook = snapshot.Discovery.Books.FirstOrDefault(item =>
                        string.Equals(item.Id.Value, bookId, StringComparison.Ordinal));
                    if (sourceBook is null)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "book_clone_source_not_found");
                    }

                    BookCloneResult result;
                    try
                    {
                        result = await bookCloneService.CloneAsync(snapshot.Discovery.Paths, sourceBook, language, cancellationToken);
                    }
                    catch (BookCloneException exception)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "book_clone_failed");
                    }

                    BackgroundTaskBridgeSnapshot? refreshTask = null;
                    string? refreshWarning = null;
                    try
                    {
                        refreshTask = BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(CancellationToken.None));
                    }
                    catch
                    {
                        refreshWarning = "Library refresh could not start.";
                    }

                    return BridgeResponse.Succeeded(request.Id, "book.clone.completed", new
                    {
                        sourceBookId = result.SourceBook.Id.Value,
                        sourceBookName = result.SourceBook.Name,
                        languageCode = result.Language.Code,
                        languageName = result.Language.Name,
                        destinationBookId = result.DestinationBook.Id.Value,
                        destinationBookName = result.DestinationBook.Name,
                        refreshTask,
                        refreshWarning
                    });
                }
            }
            if (request.Command == "task.get")
            {
                if (backgroundTaskManager is null || request.Payload is not { } taskPayload ||
                    !taskPayload.TryGetProperty("taskId", out var taskIdElement) || !TryParseTaskId(taskIdElement, out var taskId))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_task_id");
                }
                var task = await backgroundTaskManager.GetAsync(taskId, cancellationToken);
                return task is null
                    ? new BridgeResponse(Version, request.Id, false, null, "task_not_found")
                    : BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(task));
            }
            if (request.Command == "task.list")
            {
                if (backgroundTaskManager is null) return BridgeResponse.UnsupportedCommand(request.Id);
                BackgroundTaskKind? kind = null;
                if (request.Payload is { } listPayload && listPayload.TryGetProperty("kind", out var kindElement))
                {
                    if (!Enum.TryParse<BackgroundTaskKind>(kindElement.GetString(), false, out var parsedKind)) return new BridgeResponse(Version, request.Id, false, null, "invalid_task_kind");
                    kind = parsedKind;
                }
                var tasks = await backgroundTaskManager.ListAsync(kind, cancellationToken);
                return BridgeResponse.Succeeded(request.Id, "background.tasks", tasks.Select(BackgroundTaskBridgeSnapshot.From).ToArray());
            }
            if (request.Command == "task.cancel")
            {
                if (backgroundTaskManager is null || request.Payload is not { } cancelPayload || !cancelPayload.TryGetProperty("taskId", out var taskIdElement) || !TryParseTaskId(taskIdElement, out var taskId)) return new BridgeResponse(Version, request.Id, false, null, "invalid_task_id");
                var task = await backgroundTaskManager.CancelAsync(taskId, cancellationToken);
                return task is null ? new BridgeResponse(Version, request.Id, false, null, "task_not_found") : BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(task));
            }
            if (request.Command == "cache.clear")
            {
                if (backgroundTaskManager is null) return BridgeResponse.UnsupportedCommand(request.Id);
                try
                {
                    var task = await backgroundTaskManager.StartAsync(
                        BackgroundTaskKind.CacheCleanup,
                        "cache-cleanup",
                        "Library",
                        new CacheCleanupRequest(),
                        cancellationToken: cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(task));
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.ProcessingSession)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "cache_cleanup_processing_active");
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.LibraryRefresh)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "cache_cleanup_refresh_active");
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.ProductionAction)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "cache_cleanup_production_active");
                }
            }
            if (request.Command == "cache.clear.result")
            {
                if (backgroundTaskManager is null || request.Payload is not { } resultPayload ||
                    !resultPayload.TryGetProperty("taskId", out var taskIdElement) ||
                    !TryParseTaskId(taskIdElement, out var taskId))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_task_id");
                }

                var task = await backgroundTaskManager.GetAsync(taskId, cancellationToken);
                if (task is null || task.Kind != BackgroundTaskKind.CacheCleanup || task.State != BackgroundTaskState.Completed)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "task_not_completed");
                }
                if (!backgroundTaskManager.TryGetResult<CacheCleanupResult>(taskId, out var result) || result is null)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "cache_cleanup_failed");
                }
                return BridgeResponse.Succeeded(request.Id, "cache.cleanup.result", result);
            }
            if (request.Command == "book.validate")
            {
                if (applicationLoadCoordinator is null || request.Payload is not { } validationPayload || !validationPayload.TryGetProperty("bookId", out var bookId) || string.IsNullOrWhiteSpace(bookId.GetString())) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
            }

            if (request.Command == "book.keywords.save" && bookKeywordPreviewService is null)
            {
                return new BridgeResponse(Version, request.Id, false, null, "keyword_preview_required");
            }

            if (bookKeywordPreviewService is not null && request.Command is
                "book.keywords.shuffle" or "book.keywords.preview.open" or "book.keywords.preview.update-ads-asin" or "book.keywords.save")
            {
                if (applicationLoadCoordinator is null || request.Payload is not { } keywordPayload ||
                    !TryGetRequiredString(keywordPayload, "bookId", out var keywordBookId) ||
                    !TryGetOptionalInt64(keywordPayload, "clientRevision", out var clientRevision))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_keyword_builder");
                }

                var current = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                if (current is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                var keywordBook = current.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, keywordBookId, StringComparison.Ordinal));
                if (keywordBook is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");

                try
                {
                    if (request.Command == "book.keywords.shuffle")
                    {
                        if (!TryGetStringArray(keywordPayload, "bookKeywords", out var bookKeywords) ||
                            !TryGetOptionalString(keywordPayload, "adsAsin", out var adsAsin))
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_keyword_builder");
                        }
                        var result = await bookKeywordPreviewService.ShuffleAsync(keywordBook, bookKeywords, adsAsin, cancellationToken);
                        return BridgeResponse.Succeeded(request.Id, "book.keywords.preview", new
                        {
                            bookId = keywordBookId,
                            clientRevision,
                            preview = result.Preview,
                            receipt = result.Receipt,
                            receiptDigest = result.ReceiptDigest
                        });
                    }

                    if (request.Command == "book.keywords.preview.open")
                    {
                        if (!TryGetRequiredString(keywordPayload, "buildId", out var buildId))
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_keyword_builder");
                        }
                        var result = await bookKeywordPreviewService.OpenSavedAsync(keywordBook, buildId, cancellationToken);
                        return BridgeResponse.Succeeded(request.Id, "book.keywords.preview", new
                        {
                            bookId = keywordBookId,
                            clientRevision,
                            preview = result.Preview,
                            receipt = result.Receipt,
                            receiptDigest = result.ReceiptDigest,
                            saved = true
                        });
                    }

                    if (request.Command == "book.keywords.preview.update-ads-asin")
                    {
                        if (!TryGetRequiredString(keywordPayload, "baseReceipt", out var baseReceipt) ||
                            !TryGetOptionalString(keywordPayload, "adsAsin", out var adsAsin))
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_keyword_builder");
                        }
                        var result = await bookKeywordPreviewService.UpdateAdsAsinAsync(keywordBook, baseReceipt, adsAsin, cancellationToken);
                        return BridgeResponse.Succeeded(request.Id, "book.keywords.preview", new
                        {
                            bookId = keywordBookId,
                            clientRevision,
                            preview = result.Preview,
                            receipt = result.Receipt,
                            receiptDigest = result.ReceiptDigest
                        });
                    }

                    if (!TryGetRequiredString(keywordPayload, "receipt", out var receipt))
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "invalid_keyword_builder");
                    }

                    await using (await processingMutationGate.EnterAsync(cancellationToken))
                    {
                        if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                        if (await GetActiveStateWriterErrorAsync(cancellationToken) is { } activityError)
                        {
                            return new BridgeResponse(Version, request.Id, false, null, activityError);
                        }

                        var saved = await bookKeywordPreviewService.SaveAsync(keywordBook, receipt, cancellationToken);
                        object? refreshTask = null;
                        string? refreshWarning = null;
                        try
                        {
                            refreshTask = BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(CancellationToken.None));
                        }
                        catch (Exception)
                        {
                            refreshWarning = "Library refresh could not start.";
                        }
                        return BridgeResponse.Succeeded(request.Id, "book.keywords.saved", new
                        {
                            bookId = keywordBookId,
                            clientRevision,
                            keywordBuilder = saved.KeywordBuilder,
                            disposition = saved.Disposition,
                            refreshTask,
                            refreshWarning
                        });
                    }
                }
                catch (KeywordPreviewException exception)
                {
                    return KeywordPreviewFailure(request.Id, exception);
                }
                catch (BookKeywordBuilderValidationException exception)
                {
                    return KeywordBuilderFailure(request.Id, exception);
                }
            }

            if (request.Command is "book.metadata.save" or "book.brand.assign" or "book.brand.unassign" or "brand.author.save")
            {
                if (applicationLoadCoordinator is null || bookCatalogMetadataService is null || request.Payload is not { } metadataPayload)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_catalog_metadata_request");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                    if (request.Command == "book.keywords.save" && await GetActiveStateWriterErrorAsync(cancellationToken) is { } activityError)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, activityError);
                    }
                    var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                    if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");

                    try
                    {
                        if (request.Command == "brand.author.save")
                        {
                            if (!TryGetRequiredString(metadataPayload, "brandName", out var brandName) ||
                                !TryGetOptionalString(metadataPayload, "author", out var author))
                            {
                                return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_author");
                            }
                            var brand = snapshot.Discovery.Brands.FirstOrDefault(item => string.Equals(item.Name, brandName, StringComparison.Ordinal));
                            if (brand is null) return new BridgeResponse(Version, request.Id, false, null, "brand_not_found");
                            await bookCatalogMetadataService.SaveBrandAuthorAsync(brand, BrandMetadata.Create(author).Author, cancellationToken);
                        }
                        else
                        {
                            if (!TryGetRequiredString(metadataPayload, "bookId", out var bookId))
                            {
                                return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                            }
                            var book = snapshot.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookId, StringComparison.Ordinal));
                            if (book is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");

                            if (request.Command == "book.metadata.save")
                            {
                                if (!TryGetOptionalString(metadataPayload, "title", out var title) ||
                                    !TryGetOptionalString(metadataPayload, "subtitle", out var subtitle) ||
                                    !TryGetOptionalString(metadataPayload, "subcover", out var subcover) ||
                                    !TryGetOptionalString(metadataPayload, "asin", out var asin) ||
                                    !TryGetOptionalString(metadataPayload, "description", out var description) ||
                                    !TryGetOptionalString(metadataPayload, "author", out var bookAuthor))
                                {
                                    return new BridgeResponse(Version, request.Id, false, null, "invalid_book_metadata");
                                }
                                await bookCatalogMetadataService.SaveBookMetadataAsync(
                                    book,
                                    new BookProductionMetadata(title, subtitle, subcover, description, bookAuthor)
                                    {
                                        Asin = asin
                                    },
                                    cancellationToken);
                            }
                            else if (request.Command == "book.brand.assign")
                            {
                                if (!TryGetRequiredString(metadataPayload, "brandName", out var brandName))
                                {
                                    return new BridgeResponse(Version, request.Id, false, null, "brand_not_found");
                                }
                                var brand = snapshot.Discovery.Brands.FirstOrDefault(item => string.Equals(item.Name, brandName, StringComparison.Ordinal));
                                if (brand is null) return new BridgeResponse(Version, request.Id, false, null, "brand_not_found");
                                await bookCatalogMetadataService.AssignBrandAsync(book, brand, cancellationToken);
                            }
                            else
                            {
                                await bookCatalogMetadataService.UnassignBrandAsync(book, cancellationToken);
                            }
                        }
                    }
                    catch (BookCatalogMetadataException exception)
                    {
                        var errorPayload = exception.ValidationErrors is { Count: > 0 } validationErrors
                            ? new
                            {
                                policyVersion = 1,
                                validationErrors = validationErrors.Select(error =>
                                {
                                    var item = new Dictionary<string, object?>
                                    {
                                        ["field"] = error.Field,
                                        ["code"] = error.Code,
                                        ["message"] = error.Message
                                    };
                                    if (error.Tokens is { Count: > 0 }) item["tokens"] = error.Tokens;
                                    return item;
                                }).ToArray()
                            }
                            : null;
                        return new BridgeResponse(Version, request.Id, false, null, exception.Code, errorPayload);
                    }
                    catch (ArgumentException)
                    {
                        return new BridgeResponse(Version, request.Id, false, null,
                            request.Command switch
                            {
                                "brand.author.save" => "invalid_brand_author",
                                "book.keywords.save" => "invalid_keyword_builder",
                                _ => "invalid_book_metadata"
                            });
                    }
                    catch (JsonException)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "brand_metadata_invalid");
                    }

                    return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
                }
            }

            if (request.Command == "book.cover.select")
            {
                if (applicationLoadCoordinator is null || coverSelectionService is null || request.Payload is not { } coverPayload ||
                    !coverPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !coverPayload.TryGetProperty("coverReference", out var coverElement) || string.IsNullOrWhiteSpace(coverElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_cover_selection");
                }

                var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                var book = snapshot.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookIdElement.GetString(), StringComparison.Ordinal));
                if (book is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                var summary = snapshot.BookSummaries.FirstOrDefault(item => item.BookId == book.Id);
                var candidates = summary?.CoverCandidates?.Select(reference => new PrintableBook.Core.Domain.Books.BookAsset(reference, PrintableBook.Core.Domain.Books.BookAssetKind.Cover)).ToArray() ?? [];
                if (!candidates.Any(candidate => string.Equals(candidate.Reference, coverElement.GetString(), StringComparison.OrdinalIgnoreCase)))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_cover_selection");
                }

                try
                {
                    await coverSelectionService.SelectAsync(book, coverElement.GetString()!, candidates, cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
                }
                catch (ArgumentException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_cover_selection");
                }
            }

            if (request.Command == "book.interior.frame-mode.set")
            {
                if (applicationLoadCoordinator is null || interiorFrameModeService is null || request.Payload is not { } frameModePayload ||
                    !frameModePayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !frameModePayload.TryGetProperty("sourceReference", out var sourceElement) || string.IsNullOrWhiteSpace(sourceElement.GetString()) ||
                    !frameModePayload.TryGetProperty("mode", out var modeElement) || !TryParseFrameMode(modeElement, out var mode))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_frame_mode");
                }

                var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                var book = snapshot.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookIdElement.GetString(), StringComparison.Ordinal));
                if (book is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                var summary = snapshot.BookSummaries.FirstOrDefault(item => item.BookId == book.Id);
                var source = summary?.InteriorSourcePages?.FirstOrDefault(item => string.Equals(item.SourceReference, sourceElement.GetString(), StringComparison.OrdinalIgnoreCase));
                if (source is null) return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_frame_mode");

                try
                {
                    await interiorFrameModeService.SetAsync(book, new PrintableBook.Core.Abstractions.FileReference(source.SourceReference), mode, cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
                }
                catch (ArgumentException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_frame_mode");
                }
            }

            if (request.Command == "book.interior.settings.save")
            {
                if (applicationLoadCoordinator is null || bookInteriorSettingsService is null || request.Payload is not { } settingsPayload ||
                    !settingsPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");

                    var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                    if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                    var book = snapshot.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookIdElement.GetString(), StringComparison.Ordinal));
                    var summary = book is null ? null : snapshot.BookSummaries.FirstOrDefault(item => item.BookId == book.Id);
                    if (book is null || summary is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");

                    bool? hasBackground = null;
                    if (settingsPayload.TryGetProperty("hasBackground", out var backgroundElement))
                    {
                        if (backgroundElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                        }
                        hasBackground = backgroundElement.GetBoolean();
                    }

                    bool? hasIntro = null;
                    if (settingsPayload.TryGetProperty("hasIntro", out var introElement))
                    {
                        if (introElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                        }
                        hasIntro = introElement.GetBoolean();
                    }

                    IReadOnlyList<PrintableBook.Core.Abstractions.FileReference>? introInteriorSources = null;
                    if (settingsPayload.TryGetProperty("introSourceReferences", out var introSourcesElement))
                    {
                        if (introSourcesElement.ValueKind != JsonValueKind.Array)
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                        }

                        var sources = new List<PrintableBook.Core.Abstractions.FileReference>();
                        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var sourceElement in introSourcesElement.EnumerateArray())
                        {
                            if (sourceElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(sourceElement.GetString()) || !unique.Add(sourceElement.GetString()!))
                            {
                                return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                            }

                            var source = summary.InteriorSourcePages?.FirstOrDefault(item => string.Equals(item.SourceReference, sourceElement.GetString(), StringComparison.OrdinalIgnoreCase));
                            if (source is null) return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                            sources.Add(new PrintableBook.Core.Abstractions.FileReference(source.SourceReference));
                        }
                        introInteriorSources = sources;
                    }

                    var changes = new List<InteriorAssetSettingsChange>();
                    if (settingsPayload.TryGetProperty("assets", out var assetsElement))
                    {
                        if (assetsElement.ValueKind != JsonValueKind.Array)
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                        }

                        var knownSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var assetElement in assetsElement.EnumerateArray())
                        {
                            if (assetElement.ValueKind != JsonValueKind.Object ||
                                !assetElement.TryGetProperty("sourceReference", out var sourceElement) || string.IsNullOrWhiteSpace(sourceElement.GetString()) ||
                                !knownSources.Add(sourceElement.GetString()!))
                            {
                                return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                            }

                            var source = summary.InteriorSourcePages?.FirstOrDefault(item => string.Equals(item.SourceReference, sourceElement.GetString(), StringComparison.OrdinalIgnoreCase));
                            if (source is null) return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");

                            bool? isActive = null;
                            if (assetElement.TryGetProperty("active", out var activeElement))
                            {
                                if (activeElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                                {
                                    return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                                }
                                isActive = activeElement.GetBoolean();
                            }

                            FrameMode? frameMode = null;
                            if (assetElement.TryGetProperty("frameMode", out var modeElement))
                            {
                                if (!TryParseFrameMode(modeElement, out var parsedMode))
                                {
                                    return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                                }
                                frameMode = parsedMode;
                            }

                            if (isActive is null && frameMode is null)
                            {
                                return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                            }

                            changes.Add(new InteriorAssetSettingsChange(new PrintableBook.Core.Abstractions.FileReference(source.SourceReference), isActive, frameMode));
                        }
                    }

                    if (hasBackground is null && hasIntro is null && introInteriorSources is null && changes.Count == 0)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                    }

                    try
                    {
                        await bookInteriorSettingsService.SaveAsync(book, new BookInteriorSettingsChange(hasBackground, changes, hasIntro, introInteriorSources), cancellationToken);
                    }
                    catch (ArgumentException)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "invalid_book_interior_settings");
                    }
                }

                return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
            }

            if (request.Command == "book.interior.shuffle")
            {
                if (applicationLoadCoordinator is null || interiorShuffleService is null || request.Payload is not { } shufflePayload ||
                    !shufflePayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_shuffle");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                    if (await GetActiveStateWriterErrorAsync(cancellationToken) is { } activityError)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, activityError);
                    }

                    var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                    if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                    var book = snapshot.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookIdElement.GetString(), StringComparison.Ordinal));
                    var summary = book is null ? null : snapshot.BookSummaries.FirstOrDefault(item => item.BookId == book.Id);
                    if (book is null || summary is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                    if (!summary.WorkspaceStateAvailable) return new BridgeResponse(Version, request.Id, false, null, "workspace_state_unavailable");

                    try
                    {
                        await interiorShuffleService.RandomizeAsync(book, cancellationToken);
                    }
                    catch (InteriorShuffleException exception)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                    }
                }

                return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
            }

            if (request.Command is "book.background.set" or "book.interior.active.set")
            {
                if (applicationLoadCoordinator is null || bookInteriorSettingsService is null || request.Payload is not { } settingsPayload ||
                    !settingsPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, request.Command == "book.background.set" ? "invalid_book_background" : "invalid_interior_activation");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");

                    var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                    if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                    var book = snapshot.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookIdElement.GetString(), StringComparison.Ordinal));
                    if (book is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");

                    try
                    {
                        if (request.Command == "book.background.set")
                        {
                            if (!settingsPayload.TryGetProperty("enabled", out var enabledElement) || enabledElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                            {
                                return new BridgeResponse(Version, request.Id, false, null, "invalid_book_background");
                            }
                            await bookInteriorSettingsService.SetHasBackgroundAsync(book, enabledElement.GetBoolean(), cancellationToken);
                        }
                        else
                        {
                            if (!settingsPayload.TryGetProperty("sourceReference", out var sourceElement) || string.IsNullOrWhiteSpace(sourceElement.GetString()) ||
                                !settingsPayload.TryGetProperty("active", out var activeElement) || activeElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                            {
                                return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_activation");
                            }
                            var summary = snapshot.BookSummaries.FirstOrDefault(item => item.BookId == book.Id);
                            var source = summary?.InteriorSourcePages?.FirstOrDefault(item => string.Equals(item.SourceReference, sourceElement.GetString(), StringComparison.OrdinalIgnoreCase));
                            if (source is null) return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_activation");
                            await bookInteriorSettingsService.SetActiveAsync(book, new PrintableBook.Core.Abstractions.FileReference(source.SourceReference), activeElement.GetBoolean(), cancellationToken);
                        }
                    }
                    catch (ArgumentException)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, request.Command == "book.background.set" ? "invalid_book_background" : "invalid_interior_activation");
                    }
                }

                return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(await applicationLoadCoordinator.StartRefreshAsync(cancellationToken)));
            }

            if (request.Command == "book.output.open-folder")
            {
                if (applicationLoadCoordinator is null || outputActionService is null || request.Payload is not { } folderPayload ||
                    !folderPayload.TryGetProperty("bookId", out var folderBookIdElement) || string.IsNullOrWhiteSpace(folderBookIdElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_output_action");
                }

                var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                var bookId = folderBookIdElement.GetString()!;
                var discoveredBook = snapshot?.Discovery.Books.FirstOrDefault(item => item.Id.Value == bookId);
                var summary = snapshot?.BookSummaries.FirstOrDefault(item => item.BookId.Value == bookId);
                if (discoveredBook is null || summary is null)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_folder_not_found");
                }

                var outputDirectoryPath = Path.GetFullPath(Path.Combine(discoveredBook.Directory.Value, "Output"));
                var currentOutputs = summary.OutputSummaries?
                    .Where(item => item.ArtifactKind is "Cover" or "Interior")
                    .GroupBy(item => item.ArtifactKind, StringComparer.Ordinal)
                    .Select(group => group
                        .OrderByDescending(item => item.GeneratedAt ?? DateTimeOffset.MinValue)
                        .ThenByDescending(item => item.ArtifactReference, StringComparer.Ordinal)
                        .First())
                    .ToArray() ?? [];
                var existingOutputs = currentOutputs
                    .Where(item => File.Exists(item.ArtifactReference))
                    .ToArray();
                if (existingOutputs.Any(item => !string.Equals(
                        Path.GetDirectoryName(Path.GetFullPath(item.ArtifactReference)),
                        outputDirectoryPath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_folder_inconsistent");
                }
                if (!Directory.Exists(outputDirectoryPath) || existingOutputs.Length == 0)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_folder_not_found");
                }

                try
                {
                    await outputActionService.OpenFolderAsync(new DirectoryReference(outputDirectoryPath), cancellationToken);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_launch_failed");
                }
                return BridgeResponse.Succeeded(request.Id, "book.output.action.completed", new { bookId });
            }

            if (request.Command == "book.interior.open-folder")
            {
                if (applicationLoadCoordinator is null || outputActionService is null || request.Payload is not { } interiorFolderPayload ||
                    !interiorFolderPayload.TryGetProperty("bookId", out var interiorFolderBookIdElement) || string.IsNullOrWhiteSpace(interiorFolderBookIdElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_folder_action");
                }

                var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                var bookId = interiorFolderBookIdElement.GetString()!;
                var discoveredBook = snapshot?.Discovery.Books.FirstOrDefault(item => item.Id.Value == bookId);
                var summary = snapshot?.BookSummaries.FirstOrDefault(item => item.BookId.Value == bookId);
                if (discoveredBook is null || summary is null)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "interior_folder_not_found");
                }

                var interiorDirectoryPath = ResolveInteriorSourceDirectory(discoveredBook, summary);
                if (interiorDirectoryPath is null)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "interior_folder_not_found");
                }

                try
                {
                    await outputActionService.OpenFolderAsync(new DirectoryReference(interiorDirectoryPath), cancellationToken);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "interior_folder_launch_failed");
                }
                return BridgeResponse.Succeeded(request.Id, "book.interior.folder.opened", new { bookId });
            }

            if (request.Command is "book.output.preview" or "book.output.open" or "book.output.reveal" or "book.output.copy-path")
            {
                if (applicationLoadCoordinator is null || outputActionService is null || request.Payload is not { } outputPayload ||
                    !outputPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !outputPayload.TryGetProperty("artifactReference", out var artifactElement) || string.IsNullOrWhiteSpace(artifactElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_output_action");
                }

                var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                var book = snapshot?.BookSummaries.FirstOrDefault(item => item.BookId.Value == bookIdElement.GetString());
                var artifact = artifactElement.GetString()!;
                if (book is null || !book.PublishedArtifacts.Contains(artifact, StringComparer.Ordinal) || !File.Exists(artifact))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_not_found");
                }

                var fallbackToOriginal = false;
                var target = artifact;
                if (request.Command == "book.output.preview")
                {
                    var output = book.OutputSummaries?.FirstOrDefault(item =>
                        string.Equals(item.ArtifactReference, artifact, StringComparison.Ordinal));
                    if (output is null || output.VerificationStatus is "Missing" or "Invalid")
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "output_not_previewable");
                    }
                    if (output.PreviewState == "Ready" &&
                        output.PreviewArtifactReference is { Length: > 0 } previewArtifact &&
                        File.Exists(previewArtifact))
                    {
                        target = previewArtifact;
                    }
                    else
                    {
                        fallbackToOriginal = true;
                    }
                }

                var file = new PrintableBook.Core.Abstractions.FileReference(target);
                try
                {
                    if (request.Command is "book.output.preview" or "book.output.open") await outputActionService.OpenAsync(file, cancellationToken);
                    if (request.Command == "book.output.reveal") await outputActionService.RevealAsync(file, cancellationToken);
                    if (request.Command == "book.output.copy-path") await outputActionService.CopyPathAsync(file, cancellationToken);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_launch_failed");
                }
                return BridgeResponse.Succeeded(request.Id, "book.output.action.completed", new { fallbackToOriginal });
            }

            if (request.Command is "process.get" or "process.cancel" or "process.start")
            {
                if (processSessionService is null) return BridgeResponse.UnsupportedCommand(request.Id);
                try
                {
                    ProcessSessionSnapshot process;
                    if (request.Command == "process.start")
                    {
                        await using (await processingMutationGate.EnterAsync(cancellationToken))
                        {
                            process = await StartProcessAsync(request, processSessionService, applicationLoadCoordinator, cancellationToken);
                        }
                    }
                    else
                    {
                        process = request.Command == "process.get"
                            ? await processSessionService.GetAsync(cancellationToken)
                            : await processSessionService.CancelAsync(cancellationToken);
                    }
                    return BridgeResponse.Succeeded(request.Id, "process.snapshot", process);
                }
                catch (ArgumentException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Message);
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.CacheCleanup)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "cache_cleanup_active");
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.ProductionAction)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "production_action_active");
                }
                catch (InvalidOperationException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Message);
                }
            }

            if (request.Command == "brand.validate")
            {
                if (applicationLoadCoordinator is null || brandValidationService is null || request.Payload is not { } validationPayload ||
                    !validationPayload.TryGetProperty("brandName", out var brandNameElement) || string.IsNullOrWhiteSpace(brandNameElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_validation");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");

                    var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                    if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                    var brand = snapshot.Discovery.Brands.FirstOrDefault(item => string.Equals(item.Name, brandNameElement.GetString(), StringComparison.Ordinal));
                    if (brand is null) return new BridgeResponse(Version, request.Id, false, null, "brand_not_found");

                    return BridgeResponse.Succeeded(
                        request.Id,
                        "brand.validation.result",
                        await brandValidationService.ValidateAsync(brand.Directory, snapshot.GlobalSettings, cancellationToken));
                }
            }

            if (request.Command == "book.brand.templates.copy")
            {
                if (applicationLoadCoordinator is null || brandTemplateCopyService is null || request.Payload is not { } copyPayload ||
                    !copyPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_template_copy");
                }

                await using var mutation = await processingMutationGate.EnterAsync(cancellationToken);
                if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                var snapshot = await applicationLoadCoordinator.GetFreshAsync(cancellationToken);
                var legacyBrandName = copyPayload.TryGetProperty("brandName", out var brandNameElement) && brandNameElement.ValueKind == JsonValueKind.String
                    ? brandNameElement.GetString()
                    : null;
                var resolution = BookBrandExecutionResolver.ResolveBatch(snapshot, [bookIdElement.GetString()!], legacyBrandName);
                if (!resolution.IsSuccess) return new BridgeResponse(Version, request.Id, false, null, resolution.Failure!.Code);

                var resolved = resolution.Books[0];
                var book = resolved.Book;
                var bookSummary = resolved.Summary;
                var brand = resolved.Brand;
                if (!string.Equals(bookSummary.ValidationStatus, "Ready", StringComparison.Ordinal))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "book_not_ready");
                }

                var brandSummary = snapshot.BrandSummaries?.FirstOrDefault(item => string.Equals(item.BrandName, brand.Name, StringComparison.Ordinal));
                if (brandSummary?.ValidationStatus != BrandValidationStatus.Validated)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "brand_not_validated");
                }

                return BridgeResponse.Succeeded(
                    request.Id,
                    "book.brand.templates.copied",
                    await brandTemplateCopyService.CopyAsync(brand.Directory, book.Workspace, cancellationToken));
            }

            if (request.Command == "book.production.pdf-name-suggestions.get")
            {
                if (applicationLoadCoordinator is null || productionPdfNameSuggestionService is null ||
                    request.Payload is not { } namePayload ||
                    !TryGetRequiredString(namePayload, "bookId", out var nameBookId))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_pdf_name_suggestion_request");
                }

                var regenerate = false;
                if (namePayload.TryGetProperty("regenerate", out var regenerateElement))
                {
                    if (regenerateElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "invalid_pdf_name_suggestion_request");
                    }
                    regenerate = regenerateElement.GetBoolean();
                }

                var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                var book = snapshot.Discovery.Books.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id.Value, nameBookId, StringComparison.Ordinal));
                if (book is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");

                try
                {
                    var names = await productionPdfNameSuggestionService.GetAsync(book.Id.Value, regenerate, cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "book.production.pdf-name-suggestions", names);
                }
                catch (ProductionPdfNameSuggestionException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                }
            }

            if (request.Command == "book.production.asset.import")
            {
                if (applicationLoadCoordinator is null || productionFilePicker is null || productionAssetImportService is null ||
                    request.Payload is not { } importPayload ||
                    !importPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !importPayload.TryGetProperty("assetKind", out var assetKindElement) || !TryParseProductionAssetKind(assetKindElement, out var assetKind))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_production_asset_import");
                }

                var snapshot = await applicationLoadCoordinator.GetLatestCompletedSnapshotAsync(cancellationToken);
                if (snapshot is null) return new BridgeResponse(Version, request.Id, false, null, "snapshot_unavailable");
                var book = snapshot.Discovery.Books.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id.Value, bookIdElement.GetString(), StringComparison.Ordinal));
                if (book is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");

                var definition = ProductionAssets.Get(assetKind);
                var selected = await productionFilePicker.PickPngAsync(
                    definition.FileName,
                    ProductionWorkspacePaths.TemplatesDirectory(book.Workspace),
                    cancellationToken);
                if (selected is null)
                {
                    return BridgeResponse.Succeeded(request.Id, "book.production.asset.import.cancelled", new { cancelled = true });
                }

                try
                {
                    var result = await productionAssetImportService.ImportAsync(book.Workspace, assetKind, selected, cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "book.production.asset.imported", result);
                }
                catch (ProductionAssetImportException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                }
            }

            if (request.Command == "book.production.action.start")
            {
                if (backgroundTaskManager is null || request.Payload is not { } actionPayload ||
                    !actionPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !actionPayload.TryGetProperty("action", out var actionElement) || !TryParseProductionAction(actionElement, out var action))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_production_action");
                }

                var bookId = bookIdElement.GetString()!;
                var key = $"{bookId}:{action}";
                try
                {
                    var task = await backgroundTaskManager.StartAsync(
                        BackgroundTaskKind.ProductionAction,
                        key,
                        bookId,
                        new ProductionActionRequest(bookId, action),
                        cancellationToken: cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "background.task", BackgroundTaskBridgeSnapshot.From(task));
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.ProductionAction)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "production_action_active");
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.ProcessingSession)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.CacheCleanup)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "cache_cleanup_active");
                }
            }

            if (request.Command is "s3.get" or "s3.credentials.replace" or "book.s3.check" or "book.s3.upload" or "book.s3.get" or "book.s3.cancel")
            {
                if (s3StorageService is null) return BridgeResponse.UnsupportedCommand(request.Id);
                try
                {
                    if (request.Command == "s3.get")
                    {
                        return BridgeResponse.Succeeded(request.Id, "s3.snapshot", await s3StorageService.GetOverviewAsync(cancellationToken));
                    }
                    if (request.Command == "s3.credentials.replace")
                    {
                        if (request.Payload is not { } storagePayload)
                        {
                            return new BridgeResponse(Version, request.Id, false, null, "invalid_s3_credentials");
                        }
                        var input = storagePayload.Deserialize<S3StorageCredentialInput>(JsonOptions);
                        return input is null
                            ? new BridgeResponse(Version, request.Id, false, null, "invalid_s3_credentials")
                            : BridgeResponse.Succeeded(request.Id, "s3.credentials.status", await s3StorageService.ReplaceCredentialsAsync(input, cancellationToken));
                    }
                    if (request.Payload is not { } bookPayload || !TryGetRequiredString(bookPayload, "bookId", out var storageBookId))
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                    }
                    var session = request.Command switch
                    {
                        "book.s3.check" => await s3StorageService.StartAsync(storageBookId, S3StorageAction.Check, cancellationToken),
                        "book.s3.upload" => await s3StorageService.StartAsync(storageBookId, S3StorageAction.Upload, cancellationToken),
                        "book.s3.cancel" => await s3StorageService.CancelAsync(storageBookId, cancellationToken),
                        _ => await s3StorageService.GetAsync(storageBookId, cancellationToken)
                    };
                    return BridgeResponse.Succeeded(request.Id, "book.s3.session", session);
                }
                catch (JsonException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_s3_credentials");
                }
                catch (S3StorageValidationException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                }
                catch (BackgroundTaskConflictException exception) when (exception.ActiveKind == BackgroundTaskKind.S3Storage)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "s3_operation_active");
                }
            }

            if (request.Command != "settings.save" || settingsStore is null || request.Payload is not { } payload)
            {
                return BridgeResponse.UnsupportedCommand(request.Id);
            }

            try
            {
                var settings = payload.Deserialize<GlobalSettings>(JsonOptions);
                if (settings is null) return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
                settings = GenericKeywordProfilePolicy.NormalizeForSave(settings);
                await settingsStore.SaveAsync(settings, cancellationToken);
                return BridgeResponse.Succeeded(request.Id, "settings.saved", settings);
            }
            catch (JsonException)
            {
                return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
            }
            catch (ArgumentOutOfRangeException)
            {
                return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
            }
            catch (S3StorageValidationException exception)
            {
                return new BridgeResponse(Version, request.Id, false, null, exception.Code);
            }
            catch (ArgumentException)
            {
                return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return BridgeResponse.Failed(request.Id, $"{request.Command.Replace('.', '_')}_failed", exception);
        }
    }

    private async ValueTask<bool> IsProcessingActiveAsync(CancellationToken cancellationToken)
    {
        if (processSessionService is null) return false;
        var process = await processSessionService.GetAsync(cancellationToken);
        return process.IsActive || process.IsCancelling;
    }

    private async ValueTask<string?> GetActiveStateWriterErrorAsync(CancellationToken cancellationToken)
    {
        if (backgroundTaskManager is null) return null;
        var tasks = await backgroundTaskManager.ListAsync(cancellationToken: cancellationToken);
        var active = tasks.Where(task => task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling).ToArray();
        if (active.Any(task => task.Kind == BackgroundTaskKind.ProductionAction)) return "production_action_active";
        if (active.Any(task => task.Kind == BackgroundTaskKind.CacheCleanup)) return "cache_cleanup_active";
        return null;
    }

    private static BridgeResponse RouteSynchronous(BridgeRequest request) => request.Command switch
    {
        "app.ping" => BridgeResponse.Pong(request.Id),
        "app.refresh" or "app.refresh.result" or "task.get" or "task.list" or "task.cancel" or "cache.clear" or "cache.clear.result" or "book.validate" or "book.clone" or "book.metadata.save" or "book.keywords.shuffle" or "book.keywords.preview.open" or "book.keywords.preview.update-ads-asin" or "book.keywords.save" or "book.keywords.asin-crawl.start" or "book.keywords.asin-crawl.get" or "book.keywords.asin-crawl.cancel" or "amazon.browser.open" or "amazon.browser.status" or "book.brand.assign" or "book.brand.unassign" or "book.cover.select" or "book.interior.frame-mode.set" or "book.interior.settings.save" or "book.interior.shuffle" or "book.interior.open-folder" or "book.background.set" or "book.interior.active.set" or "book.brand.templates.copy" or "book.production.pdf-name-suggestions.get" or "book.production.asset.import" or "book.production.action.start" or "book.output.preview" or "book.output.open-folder" or "book.output.open" or "book.output.reveal" or "book.output.copy-path" or "settings.save" or "process.get" or "process.cancel" or "process.start" or "brand.author.save" or "brand.validate" or "brand.clone" or "diagnostics.get" or "s3.get" or "s3.credentials.replace" or "book.s3.check" or "book.s3.upload" or "book.s3.get" or "book.s3.cancel" => new BridgeResponse(Version, request.Id, true, null, null),
        _ => BridgeResponse.UnsupportedCommand(request.Id)
    };

    private static string? ResolveInteriorSourceDirectory(DiscoveredBook book, BookDesktopSummary summary)
    {
        var bookDirectoryPath = Path.GetFullPath(book.Directory.Value);
        var sourceReferences = (summary.Assets ?? [])
            .Where(asset => string.Equals(asset.Kind, "Interior", StringComparison.OrdinalIgnoreCase))
            .Select(asset => asset.SourceReference)
            .Concat((summary.InteriorSourcePages ?? []).Select(page => page.SourceReference));
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceReference in sourceReferences)
        {
            if (string.IsNullOrWhiteSpace(sourceReference)) continue;
            try
            {
                var sourcePath = Path.GetFullPath(Path.IsPathRooted(sourceReference)
                    ? sourceReference
                    : Path.Combine(bookDirectoryPath, sourceReference));
                var directoryPath = Path.GetDirectoryName(sourcePath);
                if (directoryPath is not null &&
                    IsPathWithinDirectory(bookDirectoryPath, directoryPath) &&
                    Directory.Exists(directoryPath))
                {
                    directories.Add(directoryPath);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Ignore a stale or malformed snapshot reference and try the remaining scanned Interior assets.
            }
        }

        if (directories.Count == 0) return null;
        var commonDirectory = directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First();
        foreach (var directory in directories)
        {
            while (!IsPathWithinDirectory(commonDirectory, directory))
            {
                var parent = Directory.GetParent(commonDirectory)?.FullName;
                if (parent is null || !IsPathWithinDirectory(bookDirectoryPath, parent)) return bookDirectoryPath;
                commonDirectory = parent;
            }
        }
        return commonDirectory;
    }

    private static bool IsPathWithinDirectory(string rootPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, candidatePath);
        return relativePath == "." ||
               (!Path.IsPathRooted(relativePath) &&
                !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool TryGetRequiredString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString())) return false;
        value = element.GetString()!;
        return true;
    }

    private static bool TryGetOptionalString(JsonElement payload, string propertyName, out string? value)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return true;
    }

    private static bool TryGetOptionalInt64(JsonElement payload, string propertyName, out long? value)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static BridgeResponse KeywordPreviewFailure(string requestId, KeywordPreviewException exception) =>
        new(Version, requestId, false, null, exception.Error.Code, exception.Error);

    private static BridgeResponse KeywordBuilderFailure(string requestId, BookKeywordBuilderValidationException exception)
    {
        var error = exception.Error;
        var details = new Dictionary<string, object?>();
        if (error.OffendingWord is not null) details[error.Code == "keyword_word_too_long" ? "offendingWord" : "firstUnplacedWord"] = error.OffendingWord;
        if (error.GraphemeCount is not null) details["graphemeCount"] = error.GraphemeCount;
        if (error.MaximumCharacters is not null) details["maximumCharacters"] = error.MaximumCharacters;
        if (error.UniqueWordCount is not null) details["uniqueWordCount"] = error.UniqueWordCount;
        if (error.RequiredSlotCount is not null) details["requiredSlotCount"] = error.RequiredSlotCount;
        if (error.MaximumSlotCount is not null) details["maximumSlotCount"] = error.MaximumSlotCount;
        if (error.PackingRule is not null) details["packingRule"] = error.PackingRule;
        return new BridgeResponse(Version, requestId, false, null, error.Code, new
        {
            policyVersion = 1,
            field = "keywords",
            code = error.Code,
            message = error.Message,
            details
        });
    }

    private static bool TryGetStringArray(JsonElement payload, string propertyName, out IReadOnlyList<string> values)
    {
        values = [];
        if (!payload.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array) return false;
        var parsed = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return false;
            parsed.Add(item.GetString() ?? string.Empty);
        }
        values = parsed;
        return true;
    }

    private static bool TryParseRequest(string? json, out BridgeRequest request)
    {
        request = default!;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions)!;
            return request is not null &&
                request.Version == Version &&
                !string.IsNullOrWhiteSpace(request.Id) &&
                !string.IsNullOrWhiteSpace(request.Command);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseFrameMode(JsonElement value, out FrameMode mode)
    {
        mode = value.ValueKind is JsonValueKind.String
            ? value.GetString() switch
            {
                "enabled" => FrameMode.Enabled,
                "disabled" => FrameMode.Disabled,
                _ => default
            }
            : default;
        return value.ValueKind is JsonValueKind.String && value.GetString() is "enabled" or "disabled";
    }

    private static bool TryParseProductionAssetKind(JsonElement value, out ProductionAssetKind kind)
    {
        kind = value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "final-cover" => ProductionAssetKind.FinalCover,
                "interior-cover" => ProductionAssetKind.InteriorCover,
                "book-owner" => ProductionAssetKind.BookOwner,
                _ => default
            }
            : default;
        return value.ValueKind == JsonValueKind.String && value.GetString() is "final-cover" or "interior-cover" or "book-owner";
    }

    private static bool TryParseProductionAction(JsonElement value, out ProductionActionKind action)
    {
        action = value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "build-cover-pdf" => ProductionActionKind.BuildCoverPdf,
                "process-interior-cover" => ProductionActionKind.ProcessInteriorCover,
                "process-book-owner" => ProductionActionKind.ProcessBookOwner,
                _ => default
            }
            : default;
        return value.ValueKind == JsonValueKind.String && value.GetString() is "build-cover-pdf" or "process-interior-cover" or "process-book-owner";
    }

    private static bool TryParseTaskId(JsonElement value, out BackgroundTaskId taskId)
    {
        taskId = default;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return false;
        taskId = new BackgroundTaskId(value.GetString()!);
        return true;
    }

    private static async ValueTask<ProcessSessionSnapshot> StartProcessAsync(
        BridgeRequest request,
        IProcessSessionService sessionService,
        ApplicationLoadCoordinator? applicationLoadCoordinator,
        CancellationToken cancellationToken)
    {
        if (request.Payload is not { } payload ||
            !payload.TryGetProperty("bookIds", out var bookIdsElement) ||
            bookIdsElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("A process start request requires Book ids.");
        }

        var bookIds = bookIdsElement.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var brandName = payload.TryGetProperty("brandName", out var brandElement) && brandElement.ValueKind == JsonValueKind.String
            ? brandElement.GetString()
            : null;
        var mode = payload.TryGetProperty("mode", out var modeElement) && modeElement.ValueKind == JsonValueKind.String
            ? modeElement.GetString() switch
            {
                "interior-only" => BookProcessingMode.InteriorOnly,
                "full-book" => BookProcessingMode.FullBook,
                "production-interior" => BookProcessingMode.ProductionInterior,
                _ => throw new ArgumentException("The requested processing mode is not supported.")
            }
            : throw new ArgumentException("A process start request requires a processing mode.");

        if (applicationLoadCoordinator is not null)
        {
            var snapshot = await applicationLoadCoordinator.GetFreshAsync(cancellationToken);
            var resolution = BookBrandExecutionResolver.ResolveBatch(snapshot, bookIds, brandName);
            if (!resolution.IsSuccess) throw new ArgumentException(resolution.Failure!.Code);
        }

        return await sessionService.StartAsync(bookIds, mode, cancellationToken);
    }
}
