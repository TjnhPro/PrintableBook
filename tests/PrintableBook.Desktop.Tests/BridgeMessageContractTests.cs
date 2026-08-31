using PrintableBook.Desktop.Bridge;
using PrintableBook.Desktop.BackgroundTasks;
using PrintableBook.Desktop.Loading;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Desktop.Diagnostics;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Desktop.Tests;

public sealed class BridgeMessageContractTests
{
    [Fact]
    public void Background_task_bridge_snapshot_keeps_kind_and_state_as_stable_strings()
    {
        var dto = BackgroundTaskBridgeSnapshot.From(new BackgroundTaskSnapshot(
            new BackgroundTaskId("task-123"), BackgroundTaskKind.ProcessingSession, BackgroundTaskState.Running,
            "processing", null, "interior-pages", 2, 10, null, DateTimeOffset.UtcNow, null, null, null));

        Assert.Equal("ProcessingSession", dto.Kind);
        Assert.Equal("Running", dto.State);
    }
    [Fact]
    public void PingRequestIsRoutedWithoutReachingIntoMainWindow()
    {
        var router = new WebViewBridgeRouter();

        var response = router.Handle("""{"version":1,"id":"request-1","command":"app.ping"}""");

        Assert.True(response.Ok);
        Assert.Equal("request-1", response.Id);
        Assert.Equal("app.pong", response.Command);
    }

    [Fact]
    public void UnsupportedCommandKeepsTheRequestCorrelationId()
    {
        var response = new WebViewBridgeRouter().Handle("""{"version":1,"id":"request-2","command":"book.process"}""");

        Assert.False(response.Ok);
        Assert.Equal("request-2", response.Id);
        Assert.Equal("unsupported_command", response.Error);
    }

    [Theory]
    [InlineData("book.asset" + ".preview.get")]
    [InlineData("book.asset" + ".preview.result")]
    public void Removed_preview_commands_are_unsupported(string command)
    {
        var response = new WebViewBridgeRouter().Handle($"{{\"version\":1,\"id\":\"removed-preview\",\"command\":\"{command}\"}}");

        Assert.False(response.Ok);
        Assert.Equal("removed-preview", response.Id);
        Assert.Equal("unsupported_command", response.Error);
    }

    [Fact]
    public void BlankCommandIsRejectedAsAnInvalidRequest()
    {
        var response = new WebViewBridgeRouter().Handle("""{"version":1,"id":"request-3","command":" "}""");

        Assert.False(response.Ok);
        Assert.Null(response.Id);
        Assert.Equal("invalid_request", response.Error);
    }

    [Fact]
    public void NonStringWebViewMessageIsTranslatedIntoAnInvalidRequest()
    {
        var message = WebViewMessageReader.ReadOrNull(() => throw new ArgumentException("not a string"));
        var response = new WebViewBridgeRouter().Handle(message);

        Assert.False(response.Ok);
        Assert.Equal("invalid_request", response.Error);
    }

    [Fact]
    public async Task RefreshRequestReturnsAnAcceptedBackgroundTask()
    {
        var snapshot = new ApplicationSnapshot(
            new ApplicationDiscovery(
                new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], []),
            GlobalSettings.Default,
            [],
            DateTimeOffset.UnixEpoch);
        var router = new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(snapshot)));

        var response = await router.HandleAsync("""{"version":1,"id":"request-4","command":"app.refresh"}""");

        Assert.True(response.Ok);
        Assert.Equal("background.task", response.Command);
        Assert.Equal("LibraryRefresh", Assert.IsType<BackgroundTaskBridgeSnapshot>(response.Payload).Kind);
    }

    [Fact]
    public async Task RefreshFailureIsObservedThroughItsTaskInsteadOfEscapingTheDesktopMessageHandler()
    {
        var router = new WebViewBridgeRouter(CreateCoordinator(new ThrowingSnapshotService(new InvalidDataException("The workspace processing log is invalid."))));

        var response = await router.HandleAsync("""{"version":1,"id":"request-refresh-failure","command":"app.refresh"}""");

        Assert.True(response.Ok);
        Assert.Equal("background.task", response.Command);
        Assert.Equal("Failed", Assert.IsType<BackgroundTaskBridgeSnapshot>(response.Payload).State);
    }

    [Fact]
    public async Task RefreshResultReturnsAnExistingCompletedSnapshotWithoutStartingAnother_refresh()
    {
        var snapshot = CreateSnapshot();
        var router = new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(snapshot)));

        var accepted = await router.HandleAsync("""{"version":1,"id":"start","command":"app.refresh"}""");
        var task = Assert.IsType<BackgroundTaskBridgeSnapshot>(accepted.Payload);
        var result = await router.HandleAsync($"{{\"version\":1,\"id\":\"result\",\"command\":\"app.refresh.result\",\"payload\":{{\"taskId\":\"{task.TaskId}\"}}}}");

        Assert.Equal("app.snapshot", result.Command);
        Assert.Same(snapshot, result.Payload);
    }

    [Fact]
    public async Task Cache_clear_returns_a_cache_cleanup_background_task()
    {
        var manager = new CleanupTaskManager { CleanupState = BackgroundTaskState.Running };
        var response = await new WebViewBridgeRouter(backgroundTaskManager: manager)
            .HandleAsync("""{"version":1,"id":"clear-1","command":"cache.clear"}""");

        Assert.True(response.Ok);
        Assert.Equal("background.task", response.Command);
        Assert.Equal("CacheCleanup", Assert.IsType<BackgroundTaskBridgeSnapshot>(response.Payload).Kind);
    }

    [Fact]
    public async Task Cache_clear_duplicate_returns_the_existing_task()
    {
        var manager = new CleanupTaskManager { CleanupState = BackgroundTaskState.Running };
        var router = new WebViewBridgeRouter(backgroundTaskManager: manager);

        var first = await router.HandleAsync("""{"version":1,"id":"clear-1","command":"cache.clear"}""");
        var second = await router.HandleAsync("""{"version":1,"id":"clear-2","command":"cache.clear"}""");

        Assert.Equal(
            Assert.IsType<BackgroundTaskBridgeSnapshot>(first.Payload).TaskId,
            Assert.IsType<BackgroundTaskBridgeSnapshot>(second.Payload).TaskId);
        Assert.Equal(1, manager.CleanupStarts);
    }

    [Fact]
    public async Task Cache_clear_result_returns_typed_cleanup_result_only_after_completion()
    {
        var result = new CacheCleanupResult(10, 8, 2, 0, 4_509_715_660, []);
        var manager = new CleanupTaskManager { CleanupResult = result };
        var router = new WebViewBridgeRouter(backgroundTaskManager: manager);
        var started = await router.HandleAsync("""{"version":1,"id":"clear","command":"cache.clear"}""");
        var taskId = Assert.IsType<BackgroundTaskBridgeSnapshot>(started.Payload).TaskId;

        var response = await router.HandleAsync($"{{\"version\":1,\"id\":\"result\",\"command\":\"cache.clear.result\",\"payload\":{{\"taskId\":\"{taskId}\"}}}}");

        Assert.True(response.Ok);
        Assert.Equal("cache.cleanup.result", response.Command);
        Assert.Same(result, response.Payload);
    }

    [Fact]
    public async Task Cache_clear_result_rejects_non_cleanup_task()
    {
        var manager = new CleanupTaskManager { LookupTask = new BackgroundTaskSnapshot(new BackgroundTaskId("library"), BackgroundTaskKind.LibraryRefresh, BackgroundTaskState.Completed, "library", null, null, null, null, null, null, null, null, null) };
        var response = await new WebViewBridgeRouter(backgroundTaskManager: manager)
            .HandleAsync("""{"version":1,"id":"result","command":"cache.clear.result","payload":{"taskId":"library"}}""");

        Assert.Equal("task_not_completed", response.Error);
    }

    [Fact]
    public async Task Cache_clear_result_rejects_non_completed_cleanup_task()
    {
        var manager = new CleanupTaskManager { CleanupState = BackgroundTaskState.Running };
        var response = await new WebViewBridgeRouter(backgroundTaskManager: manager)
            .HandleAsync("""{"version":1,"id":"result","command":"cache.clear.result","payload":{"taskId":"cleanup-task"}}""");

        Assert.Equal("task_not_completed", response.Error);
    }

    [Theory]
    [InlineData(BackgroundTaskKind.ProcessingSession, "cache_cleanup_processing_active")]
    [InlineData(BackgroundTaskKind.LibraryRefresh, "cache_cleanup_refresh_active")]
    public async Task Cache_clear_returns_the_safe_error_for_an_active_conflicting_task(BackgroundTaskKind activeKind, string error)
    {
        var manager = new CleanupTaskManager { ConflictActiveKind = activeKind };
        var response = await new WebViewBridgeRouter(backgroundTaskManager: manager)
            .HandleAsync("""{"version":1,"id":"clear","command":"cache.clear"}""");

        Assert.Equal(error, response.Error);
    }

    [Fact]
    public async Task App_refresh_returns_cache_cleanup_active_while_cleanup_is_active()
    {
        var manager = new CleanupTaskManager { ConflictActiveKind = BackgroundTaskKind.CacheCleanup };
        var response = await new WebViewBridgeRouter(new ApplicationLoadCoordinator(manager), backgroundTaskManager: manager)
            .HandleAsync("""{"version":1,"id":"refresh","command":"app.refresh"}""");

        Assert.Equal("cache_cleanup_active", response.Error);
    }

    [Fact]
    public async Task Process_start_returns_cache_cleanup_active_while_cleanup_is_active()
    {
        var session = new StubProcessSessionService(new ProcessSessionSnapshot(false, false, null, null, null, []))
        {
            StartException = new BackgroundTaskConflictException(BackgroundTaskKind.ProcessingSession, BackgroundTaskKind.CacheCleanup)
        };
        var response = await new WebViewBridgeRouter(processSessionService: session)
            .HandleAsync("""{"version":1,"id":"process","command":"process.start","payload":{"bookIds":["Book One"],"mode":"interior-only"}}""");

        Assert.Equal("cache_cleanup_active", response.Error);
    }

    [Fact]
    public async Task Async_bridge_commands_are_traced_without_changing_their_response_contract()
    {
        var diagnostics = new RecordingDiagnostics();
        var response = await new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(CreateSnapshot())), diagnostics: diagnostics)
            .HandleAsync("""{"version":1,"id":"request","command":"app.refresh"}""");

        Assert.True(response.Ok);
        Assert.Contains("bridge.app.refresh", diagnostics.Operations);
    }

    [Fact]
    public async Task DiagnosticsRequest_returns_the_bounded_desktop_diagnostic_snapshot()
    {
        var diagnostics = new UiDiagnosticsService();
        diagnostics.RecordDispatcherStall(TimeSpan.FromMilliseconds(300));

        var response = await new WebViewBridgeRouter(uiDiagnosticsService: diagnostics)
            .HandleAsync("""{"version":1,"id":"diagnostics","command":"diagnostics.get"}""");

        Assert.True(response.Ok);
        Assert.Equal("diagnostics.snapshot", response.Command);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<UiDiagnosticEvent>>(response.Payload));
    }

    [Fact]
    public async Task SettingsSaveRequestIsValidatedAndOwnedByTheDesktopBridge()
    {
        var settingsStore = new StubSettingsStore();
        var router = new WebViewBridgeRouter(settingsStore: settingsStore);

        var response = await router.HandleAsync("""{"version":1,"id":"request-5","command":"settings.save","payload":{"maximumPageConcurrency":6,"artworkDetectionThreshold":20,"artworkMaximumSide":2270,"workingPageWidth":2550,"workingPageHeight":2550,"finalPageWidth":2588,"finalPageHeight":2625,"dpi":300,"interiorPdfWidthInches":8.5,"interiorPdfHeightInches":8.5}}""");

        Assert.True(response.Ok);
        Assert.Equal("settings.saved", response.Command);
        Assert.Equal(6, settingsStore.Saved!.MaximumPageConcurrency);
    }

    [Fact]
    public async Task Brand_settings_save_returns_the_exact_saved_json()
    {
        var settings = new StubBrandSettingsStore();
        var manager = new RetainedSnapshotTaskManager(CreateSnapshot());
        var router = new WebViewBridgeRouter(new ApplicationLoadCoordinator(manager), brandSettingsStore: settings);

        const string json = "{\"frame\":true}";
        var saved = await router.HandleAsync("""{"version":1,"id":"brand-save","command":"brand.settings.save","payload":{"brandName":"Brand One","json":"{\"frame\":true}"}}""");

        Assert.True(saved.Ok);
        Assert.Equal("brand.settings.saved", saved.Command);
        Assert.Equal(json, Assert.IsType<string>(saved.Payload));
        Assert.Equal(json, settings.SavedJson);
        Assert.Equal(0, manager.Starts);
    }

    [Fact]
    public async Task Brand_validate_uses_the_retained_snapshot_and_returns_the_validation_result()
    {
        var validation = new StubBrandValidationService();
        var manager = new RetainedSnapshotTaskManager(CreateSnapshot());
        var router = new WebViewBridgeRouter(
            new ApplicationLoadCoordinator(manager),
            brandValidationService: validation);

        var response = await router.HandleAsync("""{"version":1,"id":"brand-validate","command":"brand.validate","payload":{"brandName":"Brand One"}}""");

        Assert.True(response.Ok);
        Assert.Equal("brand.validation.result", response.Command);
        Assert.True(Assert.IsType<BrandValidationResult>(response.Payload).IsSuccess);
        Assert.Equal(new DirectoryReference("brands/Brand One"), validation.Directory);
        Assert.Equal(0, manager.Starts);
    }

    [Fact]
    public async Task Mutation_commands_use_only_the_retained_snapshot_before_queueing_refreshes()
    {
        var manager = new RetainedSnapshotTaskManager(CreateSnapshot());
        var cover = new StubCoverSelectionService();
        var frame = new StubInteriorFrameModeService();
        var brands = new StubBrandSettingsStore();
        var router = new WebViewBridgeRouter(new ApplicationLoadCoordinator(manager), brandSettingsStore: brands, coverSelectionService: cover, interiorFrameModeService: frame);

        var coverResponse = await router.HandleAsync("""{"version":1,"id":"cover","command":"book.cover.select","payload":{"bookId":"Book One","coverReference":"cover-a.png"}}""");
        var frameResponse = await router.HandleAsync("""{"version":1,"id":"frame","command":"book.interior.frame-mode.set","payload":{"bookId":"Book One","sourceReference":"Book interior/page-001.png","mode":"enabled"}}""");
        var getResponse = await router.HandleAsync("""{"version":1,"id":"brand-get","command":"brand.settings.get","payload":{"brandName":"Brand One"}}""");
        var saveResponse = await router.HandleAsync("""{"version":1,"id":"brand-save","command":"brand.settings.save","payload":{"brandName":"Brand One","json":"{}"}}""");

        Assert.All([coverResponse, frameResponse, getResponse, saveResponse], response => Assert.True(response.Ok));
        Assert.Equal(2, manager.Starts);
        Assert.Equal(4, manager.Lists);
        Assert.Equal(("Book One", "cover-a.png"), cover.LastSelection);
        Assert.Equal(("Book One", "Book interior/page-001.png", FrameMode.Enabled), frame.LastSelection);
        Assert.Equal("{}", brands.SavedJson);
    }

    [Fact]
    public async Task Mutation_commands_reject_missing_or_unauthorized_snapshot_authority_without_persistence()
    {
        var emptyManager = new RetainedSnapshotTaskManager(null);
        var cover = new StubCoverSelectionService();
        var frame = new StubInteriorFrameModeService();
        var brands = new StubBrandSettingsStore();
        var emptyRouter = new WebViewBridgeRouter(new ApplicationLoadCoordinator(emptyManager), brandSettingsStore: brands, coverSelectionService: cover, interiorFrameModeService: frame);

        var missingCover = await emptyRouter.HandleAsync("""{"version":1,"id":"cover","command":"book.cover.select","payload":{"bookId":"Book One","coverReference":"cover-a.png"}}""");
        var missingFrame = await emptyRouter.HandleAsync("""{"version":1,"id":"frame","command":"book.interior.frame-mode.set","payload":{"bookId":"Book One","sourceReference":"Book interior/page-001.png","mode":"auto"}}""");
        var missingBrand = await emptyRouter.HandleAsync("""{"version":1,"id":"brand","command":"brand.settings.save","payload":{"brandName":"Brand One","json":"{}"}}""");

        Assert.All([missingCover, missingFrame, missingBrand], response => Assert.Equal("snapshot_unavailable", response.Error));
        Assert.Equal(0, emptyManager.Starts);
        Assert.Null(cover.LastSelection);
        Assert.Null(frame.LastSelection);
        Assert.Null(brands.SavedJson);

        var manager = new RetainedSnapshotTaskManager(CreateSnapshot());
        var retainedRouter = new WebViewBridgeRouter(new ApplicationLoadCoordinator(manager), brandSettingsStore: brands, coverSelectionService: cover, interiorFrameModeService: frame);
        var invalidCover = await retainedRouter.HandleAsync("""{"version":1,"id":"invalid-cover","command":"book.cover.select","payload":{"bookId":"Book One","coverReference":"outside.png"}}""");
        var invalidFrame = await retainedRouter.HandleAsync("""{"version":1,"id":"invalid-frame","command":"book.interior.frame-mode.set","payload":{"bookId":"Book One","sourceReference":"outside.png","mode":"auto"}}""");
        var missingNamedBrand = await retainedRouter.HandleAsync("""{"version":1,"id":"missing-brand","command":"brand.settings.get","payload":{"brandName":"Missing"}}""");

        Assert.Equal("invalid_cover_selection", invalidCover.Error);
        Assert.Equal("invalid_interior_frame_mode", invalidFrame.Error);
        Assert.Equal("brand_not_found", missingNamedBrand.Error);
        Assert.Equal(0, manager.Starts);
    }

    [Fact]
    public async Task BookValidationRefreshesCSharpOwnedValidationForTheRequestedBook()
    {
        var id = new BookId("Book One");
        var snapshot = new ApplicationSnapshot(
            new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], []),
            GlobalSettings.Default,
            [new BookDesktopSummary(id, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 0)],
            DateTimeOffset.UnixEpoch);

        var response = await new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(snapshot)))
            .HandleAsync("""{"version":1,"id":"request-6","command":"book.validate","payload":{"bookId":"Book One"}}""");

        Assert.True(response.Ok);
        Assert.Equal("background.task", response.Command);
    }

    [Fact]
    public async Task CoverSelectionIsRoutedThroughTheCSharpOwner()
    {
        var snapshot = CreateSnapshot();
        var selection = new StubCoverSelectionService();

        var response = await new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(snapshot)), coverSelectionService: selection)
            .HandleAsync("""{"version":1,"id":"request-6a","command":"book.cover.select","payload":{"bookId":"Book One","coverReference":"cover-a.png"}}""");

        Assert.True(response.Ok);
        Assert.Equal("background.task", response.Command);
        Assert.Equal(("Book One", "cover-a.png"), selection.LastSelection);
    }

    [Theory]
    [InlineData("auto", FrameMode.Auto)]
    [InlineData("enabled", FrameMode.Enabled)]
    [InlineData("disabled", FrameMode.Disabled)]
    public async Task InteriorFrameModeSelectionUsesTheCSharpOwner(string mode, FrameMode expectedMode)
    {
        var selection = new StubInteriorFrameModeService();
        var snapshot = CreateSnapshot();
        var router = new WebViewBridgeRouter(
            CreateCoordinator(new StubSnapshotService(snapshot)),
            interiorFrameModeService: selection);

        var response = await router.HandleAsync($"{{\"version\":1,\"id\":\"request-frame-mode\",\"command\":\"book.interior.frame-mode.set\",\"payload\":{{\"bookId\":\"Book One\",\"sourceReference\":\"Book interior/page-001.png\",\"mode\":\"{mode}\"}}}}");

        Assert.True(response.Ok);
        Assert.Equal("background.task", response.Command);
        Assert.Equal(("Book One", "Book interior/page-001.png", expectedMode), selection.LastSelection);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task InteriorFrameModeSelectionRejectsInvalidModes(string mode)
    {
        var response = await new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(CreateSnapshot())), interiorFrameModeService: new StubInteriorFrameModeService())
            .HandleAsync($"{{\"version\":1,\"id\":\"request-invalid-frame-mode\",\"command\":\"book.interior.frame-mode.set\",\"payload\":{{\"bookId\":\"Book One\",\"sourceReference\":\"Book interior/page-001.png\",\"mode\":\"{mode}\"}}}}");

        Assert.False(response.Ok);
        Assert.Equal("invalid_interior_frame_mode", response.Error);
    }

    [Fact]
    public async Task InteriorFrameModeSelectionRejectsMissingSourceReference()
    {
        var response = await new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(CreateSnapshot())), interiorFrameModeService: new StubInteriorFrameModeService())
            .HandleAsync("""{"version":1,"id":"request-missing-source","command":"book.interior.frame-mode.set","payload":{"bookId":"Book One","mode":"auto"}}""");

        Assert.False(response.Ok);
        Assert.Equal("invalid_interior_frame_mode", response.Error);
    }

    [Fact]
    public async Task InteriorFrameModeSelectionMapsUnexpectedServiceFailure()
    {
        var router = new WebViewBridgeRouter(
            CreateCoordinator(new StubSnapshotService(CreateSnapshot())),
            interiorFrameModeService: new ThrowingInteriorFrameModeService());

        var response = await router.HandleAsync("""{"version":1,"id":"request-frame-failure","command":"book.interior.frame-mode.set","payload":{"bookId":"Book One","sourceReference":"Book interior/page-001.png","mode":"auto"}}""");

        Assert.False(response.Ok);
        Assert.Equal("book_interior_frame-mode_set_failed: The workspace state is unavailable.", response.Error);
    }

    [Fact]
    public async Task Book_background_and_interior_activation_mutations_persist_authorized_values_and_refresh()
    {
        var settings = new StubBookInteriorSettingsService();
        var manager = new RetainedSnapshotTaskManager(CreateSnapshot());
        var router = new WebViewBridgeRouter(new ApplicationLoadCoordinator(manager), bookInteriorSettingsService: settings);
        var background = await router.HandleAsync("""{"version":1,"id":"background","command":"book.background.set","payload":{"bookId":"Book One","enabled":true}}""");
        var active = await router.HandleAsync("""{"version":1,"id":"active","command":"book.interior.active.set","payload":{"bookId":"Book One","sourceReference":"Book interior/page-001.png","active":false}}""");
        Assert.True(background.Ok);
        Assert.True(active.Ok);
        Assert.Equal(("Book One", true), settings.Background);
        Assert.Equal(("Book One", "Book interior/page-001.png", false), settings.Active);
        Assert.Equal(2, manager.Starts);
    }

    [Fact]
    public async Task Book_interior_settings_save_persists_a_batch_and_starts_one_refresh()
    {
        var settings = new StubBookInteriorSettingsService();
        var manager = new RetainedSnapshotTaskManager(CreateSnapshot());
        var router = new WebViewBridgeRouter(new ApplicationLoadCoordinator(manager), bookInteriorSettingsService: settings);

        var response = await router.HandleAsync("""{"version":1,"id":"save","command":"book.interior.settings.save","payload":{"bookId":"Book One","hasBackground":true,"assets":[{"sourceReference":"Book interior/page-001.png","active":false,"frameMode":"enabled"}]}}""");

        Assert.True(response.Ok);
        Assert.True(settings.Batch!.HasBackground);
        var asset = Assert.Single(settings.Batch.Assets);
        Assert.Equal("Book interior/page-001.png", asset.Source.Value);
        Assert.False(asset.IsActive);
        Assert.Equal(FrameMode.Enabled, asset.FrameMode);
        Assert.Equal(1, manager.Starts);
    }

    [Fact]
    public async Task Book_interior_settings_save_persists_an_ordered_intro_selection_authorized_by_the_book_interior()
    {
        var settings = new StubBookInteriorSettingsService();
        var router = new WebViewBridgeRouter(new ApplicationLoadCoordinator(new RetainedSnapshotTaskManager(CreateSnapshot())), bookInteriorSettingsService: settings);

        var response = await router.HandleAsync("""{"version":1,"id":"save","command":"book.interior.settings.save","payload":{"bookId":"Book One","hasIntro":true,"introSourceReferences":["Book interior/page-002.png","Book interior/page-001.png"]}}""");

        Assert.True(response.Ok);
        Assert.True(settings.Batch!.HasIntro);
        Assert.Equal(["Book interior/page-002.png", "Book interior/page-001.png"], settings.Batch.IntroInteriorSources!.Select(source => source.Value));
    }

    [Fact]
    public async Task Book_interior_settings_save_rejects_intro_sources_not_authorized_by_the_book_interior()
    {
        var router = new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(CreateSnapshot())), bookInteriorSettingsService: new StubBookInteriorSettingsService());

        var response = await router.HandleAsync("""{"version":1,"id":"save","command":"book.interior.settings.save","payload":{"bookId":"Book One","introSourceReferences":["brands/Brand One/IntroTemplate/intro.png"]}}""");

        Assert.Equal("invalid_book_interior_settings", response.Error);
    }

    [Fact]
    public async Task Book_interior_settings_save_accepts_an_empty_custom_intro_selection_for_readiness_feedback()
    {
        var settings = new StubBookInteriorSettingsService();
        var router = new WebViewBridgeRouter(new ApplicationLoadCoordinator(new RetainedSnapshotTaskManager(CreateSnapshot())), bookInteriorSettingsService: settings);

        var response = await router.HandleAsync("""{"version":1,"id":"save","command":"book.interior.settings.save","payload":{"bookId":"Book One","hasIntro":true,"introSourceReferences":[]}}""");

        Assert.True(response.Ok);
        Assert.True(settings.Batch!.HasIntro);
        Assert.Empty(settings.Batch.IntroInteriorSources!);
    }

    [Fact]
    public async Task Book_interior_settings_save_rejects_duplicate_custom_intro_sources_case_insensitively()
    {
        var router = new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(CreateSnapshot())), bookInteriorSettingsService: new StubBookInteriorSettingsService());

        var response = await router.HandleAsync("""{"version":1,"id":"save","command":"book.interior.settings.save","payload":{"bookId":"Book One","hasIntro":true,"introSourceReferences":["Book interior/page-001.png","BOOK INTERIOR/PAGE-001.PNG"]}}""");

        Assert.Equal("invalid_book_interior_settings", response.Error);
    }

    [Fact]
    public async Task Book_interior_settings_save_rejects_unknown_or_empty_changes()
    {
        var router = new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(CreateSnapshot())), bookInteriorSettingsService: new StubBookInteriorSettingsService());

        var unknown = await router.HandleAsync("""{"version":1,"id":"unknown","command":"book.interior.settings.save","payload":{"bookId":"Book One","assets":[{"sourceReference":"outside.png","active":false}]}}""");
        var empty = await router.HandleAsync("""{"version":1,"id":"empty","command":"book.interior.settings.save","payload":{"bookId":"Book One","assets":[]}}""");

        Assert.Equal("invalid_book_interior_settings", unknown.Error);
        Assert.Equal("invalid_book_interior_settings", empty.Error);
    }

    [Fact]
    public async Task Book_interior_mutations_reject_active_or_cancelling_processes_without_persisting()
    {
        var settings = new StubBookInteriorSettingsService();
        var snapshot = new ProcessSessionSnapshot(true, true, null, null, null, []);
        var router = new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(CreateSnapshot())), processSessionService: new StubProcessSessionService(snapshot), bookInteriorSettingsService: settings);
        var response = await router.HandleAsync("""{"version":1,"id":"active","command":"book.interior.active.set","payload":{"bookId":"Book One","sourceReference":"Book interior/page-001.png","active":false}}""");
        Assert.Equal("processing_active", response.Error);
        Assert.Null(settings.Active);
    }

    [Fact]
    public async Task Interior_settings_save_is_rejected_when_process_start_wins_the_transition_gate()
    {
        var process = new GatedProcessSessionService();
        var settings = new GatedBookInteriorSettingsService();
        var router = CreateGatedRouter(process, settings);

        var start = router.HandleAsync(ProcessStartRequest()).AsTask();
        await process.StartEntered.Task;
        var mutation = router.HandleAsync(InteriorSettingsSaveRequest()).AsTask();
        Assert.False(mutation.IsCompleted);

        process.AllowStart.TrySetResult(null);
        Assert.True((await start).Ok);
        var response = await mutation;

        Assert.Equal("processing_active", response.Error);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public async Task Process_start_waits_for_an_inflight_interior_settings_save()
    {
        var process = new GatedProcessSessionService(pauseStart: false);
        var settings = new GatedBookInteriorSettingsService(pauseSave: true);
        var router = CreateGatedRouter(process, settings);

        var mutation = router.HandleAsync(InteriorSettingsSaveRequest()).AsTask();
        await settings.SaveEntered.Task;
        var start = router.HandleAsync(ProcessStartRequest()).AsTask();
        Assert.False(start.IsCompleted);

        settings.AllowSave.TrySetResult(null);
        Assert.True((await mutation).Ok);
        Assert.True((await start).Ok);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public async Task Process_start_releases_the_transition_gate_before_a_following_interior_mutation()
    {
        var process = new GatedProcessSessionService(pauseStart: false);
        var settings = new GatedBookInteriorSettingsService();
        var router = CreateGatedRouter(process, settings);

        Assert.True((await router.HandleAsync(ProcessStartRequest())).Ok);
        var mutation = await router.HandleAsync(InteriorSettingsSaveRequest());

        Assert.Equal("processing_active", mutation.Error);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public async Task Process_cancel_is_not_blocked_by_the_transition_gate()
    {
        var gate = new ProcessingMutationGate();
        var process = new GatedProcessSessionService(pauseStart: false);
        var router = new WebViewBridgeRouter(processSessionService: process, processingMutationGate: gate);

        await using var held = await gate.EnterAsync();
        var response = await router.HandleAsync("""{"version":1,"id":"cancel","command":"process.cancel"}""");

        Assert.True(response.Ok);
    }

    [Fact]
    public async Task ProcessStatusIsProvidedByTheCSharpSessionOwner()
    {
        var id = new BookId("Book One");
        var session = new ProcessSessionSnapshot(true, false, "Amazon", id, "interior-pages", [new ProcessQueueEntry(id, BookProcessingStatus.Running, null)]);
        var response = await new WebViewBridgeRouter(processSessionService: new StubProcessSessionService(session))
            .HandleAsync("""{"version":1,"id":"request-7","command":"process.get"}""");

        Assert.True(response.Ok);
        Assert.Equal("process.snapshot", response.Command);
        Assert.Same(session, response.Payload);
    }

    [Fact]
    public async Task ProcessStartRoutesTheExplicitInteriorOnlyModeToTheSessionOwner()
    {
        var id = new BookId("Book One");
        var session = new StubProcessSessionService(new ProcessSessionSnapshot(false, false, "Amazon", id, null, []));

        var response = await new WebViewBridgeRouter(processSessionService: session)
            .HandleAsync("""{"version":1,"id":"request-interior-only","command":"process.start","payload":{"bookIds":["Book One"],"brandName":"Amazon","mode":"interior-only"}}""");

        Assert.True(response.Ok);
        Assert.Equal(BookProcessingMode.InteriorOnly, session.LastMode);
    }

    [Fact]
    public async Task ProcessCommandsReturnTheImmediateSessionSnapshotsWithoutAWaitCommand()
    {
        var idle = new ProcessSessionSnapshot(false, false, null, null, null, []);
        var running = idle with { IsActive = true, CurrentStep = "Running" };
        var cancelling = running with { IsCancelling = true, CurrentStep = "Cancelling" };
        var session = new StubProcessSessionService(idle)
        {
            StartSnapshot = running,
            CancelSnapshot = cancelling
        };
        var router = new WebViewBridgeRouter(processSessionService: session);

        var started = await router.HandleAsync("""{"version":1,"id":"start","command":"process.start","payload":{"bookIds":["Book One"],"mode":"interior-only"}}""");
        var current = await router.HandleAsync("""{"version":1,"id":"get","command":"process.get"}""");
        var cancelled = await router.HandleAsync("""{"version":1,"id":"cancel","command":"process.cancel"}""");

        Assert.Same(running, started.Payload);
        Assert.Same(running, current.Payload);
        Assert.Same(cancelling, cancelled.Payload);
        Assert.True(((ProcessSessionSnapshot)cancelled.Payload!).IsCancelling);
    }

    private static WebViewBridgeRouter CreateGatedRouter(GatedProcessSessionService process, GatedBookInteriorSettingsService settings) =>
        new(new ApplicationLoadCoordinator(new RetainedSnapshotTaskManager(CreateSnapshot())), processSessionService: process, bookInteriorSettingsService: settings);

    private static string ProcessStartRequest() =>
        """{"version":1,"id":"start","command":"process.start","payload":{"bookIds":["Book One"],"brandName":"Brand","mode":"interior-only"}}""";

    private static string InteriorSettingsSaveRequest() =>
        """{"version":1,"id":"save","command":"book.interior.settings.save","payload":{"bookId":"Book One","assets":[{"sourceReference":"Book interior/page-001.png","active":false}]}}""";

    private sealed class StubSnapshotService(ApplicationSnapshot snapshot) : IApplicationSnapshotService
    {
        public ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

    private sealed class ThrowingSnapshotService(Exception exception) : IApplicationSnapshotService
    {
        public ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromException<ApplicationSnapshot>(exception);
    }

    private sealed class BlockingSnapshotService : IApplicationSnapshotService
    {
        private readonly TaskCompletionSource<ApplicationSnapshot> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RefreshCount { get; private set; }

        public async ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            Started.TrySetResult();
            return await completion.Task;
        }

        public void Complete(ApplicationSnapshot snapshot) => completion.TrySetResult(snapshot);
    }

    private static ApplicationLoadCoordinator CreateCoordinator(IApplicationSnapshotService snapshots) =>
        new(new SnapshotTaskManager(snapshots));

    private sealed class SnapshotTaskManager : IBackgroundTaskManager
    {
        private readonly BackgroundTaskId id = new("task-test-library");
        private readonly IApplicationSnapshotService snapshots;
        private ApplicationSnapshot? snapshot;
        private Exception? failure;

        public SnapshotTaskManager(IApplicationSnapshotService snapshots)
        {
            this.snapshots = snapshots;
            try { snapshot = snapshots.RefreshAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        }

        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default)
        {
            try { snapshot = snapshots.RefreshAsync(cancellationToken).AsTask().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
            return ValueTask.FromResult(Current());
        }
        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Current());
        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>([Current()]);
        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Current());
        public ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result) { if (snapshot is TResult typed) { result = typed; return true; } result = default; return false; }
        public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? view) where TView : class { view = null; return false; }
        private BackgroundTaskSnapshot Current() => new(id, BackgroundTaskKind.LibraryRefresh, failure is null ? BackgroundTaskState.Completed : BackgroundTaskState.Failed, "library", "Library", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, failure is null ? null : "refresh_failed", failure?.Message);
    }

    private sealed class RetainedSnapshotTaskManager(ApplicationSnapshot? snapshot) : IBackgroundTaskManager
    {
        private readonly BackgroundTaskId id = new("retained-library-task");
        public int Starts { get; private set; }
        public int Lists { get; private set; }
        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default)
        {
            Starts++;
            return ValueTask.FromResult(Current());
        }
        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Current());
        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default)
        {
            Lists++;
            return ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>(snapshot is null ? [] : [Current()]);
        }
        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Current());
        public ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result) { if (snapshot is TResult typed) { result = typed; return true; } result = default; return false; }
        public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? view) where TView : class { view = null; return false; }
        private BackgroundTaskSnapshot Current() => new(id, BackgroundTaskKind.LibraryRefresh, BackgroundTaskState.Completed, "library", "Library", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
    }

    private sealed class NoopRecoveryService : IInterruptedProcessingRecoveryService
    {
        public ValueTask RecoverAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingDiagnostics : IOperationDiagnostics
    {
        public List<string> Operations { get; } = [];
        public IDisposable Begin(string operation, string? subject = null)
        {
            Operations.Add(operation);
            return new Scope();
        }

        public void Record(string operation, string? subject = null, string? detail = null) { }

        private sealed class Scope : IDisposable { public void Dispose() { } }
    }

    private sealed class StubSettingsStore : IGlobalSettingsStore
    {
        public GlobalSettings? Saved { get; private set; }
        public ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(GlobalSettings.Default);
        public ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default) => ValueTask.FromResult(GlobalSettings.Default);
        public ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default)
        {
            Saved = settings;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubBrandSettingsStore : IBrandSettingsStore
    {
        public DirectoryReference? LoadedDirectory { get; private set; }
        public string? SavedJson { get; private set; }
        public ValueTask<string> LoadAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken = default)
        {
            LoadedDirectory = brandDirectory;
            return ValueTask.FromResult("{}");
        }
        public ValueTask SaveAsync(DirectoryReference brandDirectory, string json, CancellationToken cancellationToken = default)
        {
            LoadedDirectory = brandDirectory;
            SavedJson = json;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubBrandValidationService : IBrandValidationService
    {
        public DirectoryReference? Directory { get; private set; }

        public ValueTask<BrandValidationState> CheckStateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BrandValidationState(BrandValidationStatus.Validated));

        public ValueTask<BrandValidationResult> ValidateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default)
        {
            Directory = brandDirectory;
            return ValueTask.FromResult(new BrandValidationResult(new BrandValidationState(BrandValidationStatus.Validated), []));
        }
    }

    private sealed class StubProcessSessionService : IProcessSessionService
    {
        private readonly ProcessSessionSnapshot initialSnapshot;
        private ProcessSessionSnapshot current;

        public StubProcessSessionService(ProcessSessionSnapshot snapshot)
        {
            initialSnapshot = snapshot;
            current = snapshot;
        }

        public BookProcessingMode? LastMode { get; private set; }
        public ProcessSessionSnapshot? StartSnapshot { get; init; }
        public ProcessSessionSnapshot? CancelSnapshot { get; init; }
        public Exception? StartException { get; init; }
        public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(current);
        public ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default)
        {
            if (StartException is not null) return ValueTask.FromException<ProcessSessionSnapshot>(StartException);
            LastMode = mode;
            current = StartSnapshot ?? initialSnapshot;
            return ValueTask.FromResult(current);
        }
        public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default)
        {
            current = CancelSnapshot ?? initialSnapshot;
            return ValueTask.FromResult(current);
        }
        public ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class CleanupTaskManager : IBackgroundTaskManager
    {
        private readonly BackgroundTaskId cleanupTaskId = new("cleanup-task");
        public BackgroundTaskState CleanupState { get; init; } = BackgroundTaskState.Completed;
        public CacheCleanupResult? CleanupResult { get; init; } = new(0, 0, 0, 0, 0, []);
        public BackgroundTaskKind? ConflictActiveKind { get; init; }
        public BackgroundTaskSnapshot? LookupTask { get; init; }
        public int CleanupStarts { get; private set; }

        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default)
        {
            if (ConflictActiveKind is { } active) throw new BackgroundTaskConflictException(kind, active);
            if (kind == BackgroundTaskKind.CacheCleanup && CleanupStarts == 0) CleanupStarts++;
            return ValueTask.FromResult(Current(kind));
        }

        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BackgroundTaskSnapshot?>(LookupTask ?? (taskId == cleanupTaskId ? Current(BackgroundTaskKind.CacheCleanup) : null));
        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>([Current(BackgroundTaskKind.CacheCleanup)]);
        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Current(BackgroundTaskKind.CacheCleanup));
        public ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
        {
            if (taskId == cleanupTaskId && CleanupResult is TResult typed) { result = typed; return true; }
            result = default;
            return false;
        }
        public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? view) where TView : class { view = default; return false; }
        private BackgroundTaskSnapshot Current(BackgroundTaskKind kind) => new(cleanupTaskId, kind, kind == BackgroundTaskKind.CacheCleanup ? CleanupState : BackgroundTaskState.Completed, "cleanup", "Library", null, null, null, null, DateTimeOffset.UtcNow, CleanupState == BackgroundTaskState.Completed ? DateTimeOffset.UtcNow : null, null, null);
    }

    private sealed class StubCoverSelectionService : IBookCoverSelectionService
    {
        public (string BookId, string CoverReference)? LastSelection { get; private set; }
        public ValueTask SelectAsync(DiscoveredBook book, string coverReference, IReadOnlyList<BookAsset> discoveredCoverAssets, CancellationToken cancellationToken = default)
        {
            LastSelection = (book.Id.Value, coverReference);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubInteriorFrameModeService : IInteriorFrameModeService
    {
        public (string BookId, string SourceReference, FrameMode Mode)? LastSelection { get; private set; }

        public ValueTask SetAsync(DiscoveredBook book, FileReference source, FrameMode mode, CancellationToken cancellationToken = default)
        {
            LastSelection = (book.Id.Value, source.Value, mode);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubBookInteriorSettingsService : IBookInteriorSettingsService
    {
        public (string BookId, bool Enabled)? Background { get; private set; }
        public (string BookId, string SourceReference, bool Active)? Active { get; private set; }
        public BookInteriorSettingsChange? Batch { get; private set; }
        public ValueTask SetHasBackgroundAsync(DiscoveredBook book, bool enabled, CancellationToken cancellationToken = default) { Background = (book.Id.Value, enabled); return ValueTask.CompletedTask; }
        public ValueTask SetActiveAsync(DiscoveredBook book, FileReference source, bool isActive, CancellationToken cancellationToken = default) { Active = (book.Id.Value, source.Value, isActive); return ValueTask.CompletedTask; }
        public ValueTask SaveAsync(DiscoveredBook book, BookInteriorSettingsChange change, CancellationToken cancellationToken = default) { Batch = change; return ValueTask.CompletedTask; }
    }

    private sealed class GatedProcessSessionService(bool pauseStart = true) : IProcessSessionService
    {
        private bool active;
        public TaskCompletionSource<object?> StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> AllowStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot());

        public async ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default)
        {
            active = true;
            StartEntered.TrySetResult(null);
            if (pauseStart) await AllowStart.Task.WaitAsync(cancellationToken);
            return Snapshot();
        }

        public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default)
        {
            active = false;
            return ValueTask.FromResult(Snapshot());
        }

        public ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        private ProcessSessionSnapshot Snapshot() => new(active, false, "Brand", new BookId("Book One"), active ? "Running" : null, []);
    }

    private sealed class GatedBookInteriorSettingsService(bool pauseSave = false) : IBookInteriorSettingsService
    {
        public int SaveCalls { get; private set; }
        public TaskCompletionSource<object?> SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask SetHasBackgroundAsync(DiscoveredBook book, bool enabled, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SetActiveAsync(DiscoveredBook book, FileReference source, bool isActive, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask SaveAsync(DiscoveredBook book, BookInteriorSettingsChange change, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            SaveEntered.TrySetResult(null);
            if (pauseSave) await AllowSave.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingInteriorFrameModeService : IInteriorFrameModeService
    {
        public ValueTask SetAsync(DiscoveredBook book, FileReference source, FrameMode mode, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidDataException("The workspace state is unavailable."));
    }

    private static ApplicationSnapshot CreateSnapshot()
    {
        var id = new BookId("Book One");
        var book = new DiscoveredBook("Book One", id, new DirectoryReference("sources/Book One"), new BookWorkspace(id, new DirectoryReference("workspace"), new DirectoryReference("processed"), new DirectoryReference("temporary")));
        return new ApplicationSnapshot(
            new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [new DiscoveredBrand("Brand One", new DirectoryReference("brands/Brand One"))], [book]),
            GlobalSettings.Default,
            [new BookDesktopSummary(id, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 2, CoverCandidates: ["cover-a.png"], InteriorSourcePages:
            [
                new InteriorSourcePageSummary("Book interior/page-001.png", FrameMode.Auto, SourceKey: "Book interior/page-001.png"),
                new InteriorSourcePageSummary("Book interior/page-002.png", FrameMode.Auto, SourceKey: "Book interior/page-002.png")
            ])],
            DateTimeOffset.UnixEpoch);
    }
}
