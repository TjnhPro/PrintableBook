using System.Text.Json;
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
using PrintableBook.Desktop.BackgroundTasks;
using PrintableBook.Desktop.Updates;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Domain.Books;

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
    IBookCatalogMetadataService? bookCatalogMetadataService = null)
{
    private readonly IOperationDiagnostics diagnostics = diagnostics ?? new NoOpOperationDiagnostics();
    private readonly ProcessingMutationGate processingMutationGate = processingMutationGate ?? new ProcessingMutationGate();
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

            if (request.Command is "book.metadata.save" or "book.brand.assign" or "book.brand.unassign" or "brand.author.save")
            {
                if (applicationLoadCoordinator is null || bookCatalogMetadataService is null || request.Payload is not { } metadataPayload)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_catalog_metadata_request");
                }

                await using (await processingMutationGate.EnterAsync(cancellationToken))
                {
                    if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");
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
                            await bookCatalogMetadataService.SaveBrandAuthorAsync(brand, author, cancellationToken);
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
                                    !TryGetOptionalString(metadataPayload, "description", out var description) ||
                                    !TryGetOptionalString(metadataPayload, "author", out var bookAuthor))
                                {
                                    return new BridgeResponse(Version, request.Id, false, null, "invalid_book_metadata");
                                }
                                await bookCatalogMetadataService.SaveBookMetadataAsync(
                                    book,
                                    BookProductionMetadata.Create(title, subtitle, subcover, description, bookAuthor),
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
                        return new BridgeResponse(Version, request.Id, false, null, exception.Code);
                    }
                    catch (ArgumentException)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "invalid_book_metadata");
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

            if (request.Command is "book.output.open" or "book.output.reveal" or "book.output.copy-path")
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
                if (book is null || !book.PublishedArtifacts.Contains(artifact, StringComparer.Ordinal) || !System.IO.File.Exists(artifact))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_not_found");
                }

                var file = new PrintableBook.Core.Abstractions.FileReference(artifact);
                if (request.Command == "book.output.open") await outputActionService.OpenAsync(file, cancellationToken);
                if (request.Command == "book.output.reveal") await outputActionService.RevealAsync(file, cancellationToken);
                if (request.Command == "book.output.copy-path") await outputActionService.CopyPathAsync(file, cancellationToken);
                return BridgeResponse.Succeeded(request.Id, "book.output.action.completed", new { });
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
                            process = await StartProcessAsync(request, processSessionService, cancellationToken);
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
                    !copyPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !copyPayload.TryGetProperty("brandName", out var brandNameElement) || string.IsNullOrWhiteSpace(brandNameElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_template_copy");
                }

                await using var mutation = await processingMutationGate.EnterAsync(cancellationToken);
                if (await IsProcessingActiveAsync(cancellationToken)) return new BridgeResponse(Version, request.Id, false, null, "processing_active");
                var snapshot = await applicationLoadCoordinator.GetFreshAsync(cancellationToken);

                var book = snapshot.Discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookIdElement.GetString(), StringComparison.Ordinal));
                var bookSummary = book is null ? null : snapshot.BookSummaries.FirstOrDefault(item => item.BookId == book.Id);
                if (book is null || bookSummary is null) return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                var execution = BookBrandExecutionPolicy.Evaluate(bookSummary.AssignedBrand, bookSummary.AssignmentStatus, brandNameElement.GetString());
                if (!execution.IsAllowed) return new BridgeResponse(Version, request.Id, false, null, execution.Code);
                if (!string.Equals(bookSummary.ValidationStatus, "Ready", StringComparison.Ordinal))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "book_not_ready");
                }

                var brand = snapshot.Discovery.Brands.FirstOrDefault(item => string.Equals(item.Name, brandNameElement.GetString(), StringComparison.Ordinal));
                if (brand is null) return new BridgeResponse(Version, request.Id, false, null, "brand_not_found");
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

            if (request.Command != "settings.save" || settingsStore is null || request.Payload is not { } payload)
            {
                return BridgeResponse.UnsupportedCommand(request.Id);
            }

            try
            {
                var settings = payload.Deserialize<GlobalSettings>(JsonOptions);
                if (settings is null) return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
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

    private static BridgeResponse RouteSynchronous(BridgeRequest request) => request.Command switch
    {
        "app.ping" => BridgeResponse.Pong(request.Id),
        "app.refresh" or "app.refresh.result" or "task.get" or "task.list" or "task.cancel" or "cache.clear" or "cache.clear.result" or "book.validate" or "book.metadata.save" or "book.brand.assign" or "book.brand.unassign" or "book.cover.select" or "book.interior.frame-mode.set" or "book.interior.settings.save" or "book.background.set" or "book.interior.active.set" or "book.brand.templates.copy" or "book.production.asset.import" or "book.production.action.start" or "book.output.open" or "book.output.reveal" or "book.output.copy-path" or "settings.save" or "process.get" or "process.cancel" or "process.start" or "brand.author.save" or "brand.validate" or "diagnostics.get" => new BridgeResponse(Version, request.Id, true, null, null),
        _ => BridgeResponse.UnsupportedCommand(request.Id)
    };

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
                "auto" => FrameMode.Auto,
                "enabled" => FrameMode.Enabled,
                "disabled" => FrameMode.Disabled,
                _ => default
            }
            : default;
        return value.ValueKind is JsonValueKind.String && value.GetString() is "auto" or "enabled" or "disabled";
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

    private static async ValueTask<ProcessSessionSnapshot> StartProcessAsync(BridgeRequest request, IProcessSessionService sessionService, CancellationToken cancellationToken)
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
        return await sessionService.StartAsync(bookIds, brandName, mode, cancellationToken);
    }
}
