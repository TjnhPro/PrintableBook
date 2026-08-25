using PrintableBook.Desktop.Bridge;
using PrintableBook.Desktop.BackgroundTasks;
using PrintableBook.Desktop.Loading;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Desktop.Diagnostics;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
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
        public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(current);
        public ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default)
        {
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
            [new BookDesktopSummary(id, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 1, CoverCandidates: ["cover-a.png"], InteriorSourcePages: [new InteriorSourcePageSummary("Book interior/page-001.png", FrameMode.Auto)])],
            DateTimeOffset.UnixEpoch);
    }
}
