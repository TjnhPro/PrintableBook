using PrintableBook.Desktop.Bridge;
using PrintableBook.Desktop.Loading;
using PrintableBook.Core.Application.Diagnostics;
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
    public async Task RefreshRequestReturnsTheSnapshotFromTheApplicationLayer()
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
        Assert.Equal("app.snapshot", response.Command);
        Assert.Same(snapshot, response.Payload);
    }

    [Fact]
    public async Task RefreshFailureReturnsACorrelatedBridgeErrorInsteadOfEscapingTheDesktopMessageHandler()
    {
        var router = new WebViewBridgeRouter(CreateCoordinator(new ThrowingSnapshotService(new InvalidDataException("The workspace processing log is invalid."))));

        var response = await router.HandleAsync("""{"version":1,"id":"request-refresh-failure","command":"app.refresh"}""");

        Assert.False(response.Ok);
        Assert.Equal("request-refresh-failure", response.Id);
        Assert.Equal("app_refresh_failed: The workspace processing log is invalid.", response.Error);
    }

    [Fact]
    public async Task ConcurrentRefreshCommandsUseOneCoordinatorOwnedSnapshotScan()
    {
        var snapshots = new BlockingSnapshotService();
        var router = new WebViewBridgeRouter(CreateCoordinator(snapshots));

        var first = router.HandleAsync("""{"version":1,"id":"first","command":"app.refresh"}""").AsTask();
        await snapshots.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = router.HandleAsync("""{"version":1,"id":"second","command":"app.refresh"}""").AsTask();

        Assert.Equal(1, snapshots.RefreshCount);
        snapshots.Complete(CreateSnapshot());

        Assert.Equal("app.snapshot", (await first).Command);
        Assert.Equal("app.snapshot", (await second).Command);
        Assert.Equal(1, snapshots.RefreshCount);
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
        Assert.Equal("app.snapshot", response.Command);
    }

    [Fact]
    public async Task CoverSelectionIsRoutedThroughTheCSharpOwner()
    {
        var id = new BookId("Book One");
        var snapshot = new ApplicationSnapshot(
            new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], []),
            GlobalSettings.Default,
            [new BookDesktopSummary(id, "Needs selection", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 0)],
            DateTimeOffset.UnixEpoch);
        var selection = new StubCoverSelectionService();

        var response = await new WebViewBridgeRouter(CreateCoordinator(new StubSnapshotService(snapshot)), coverSelectionService: selection)
            .HandleAsync("""{"version":1,"id":"request-6a","command":"book.cover.select","payload":{"bookId":"Book One","coverReference":"cover-a.png"}}""");

        Assert.True(response.Ok);
        Assert.Equal("app.snapshot", response.Command);
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
        Assert.Equal("app.snapshot", response.Command);
        Assert.Same(snapshot, response.Payload);
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
    public async Task AssetPreviewIsRoutedOnlyThroughTheCSharpPreviewOwner()
    {
        var preview = new BookAssetPreview("Book One", "Book interior/page-001.png", 120, 120, "data:image/png;base64,preview");
        var service = new StubAssetPreviewService(preview);
        var response = await new WebViewBridgeRouter(assetPreviewService: service)
            .HandleAsync("""{"version":1,"id":"preview","command":"book.asset.preview.get","payload":{"bookId":"Book One","sourceReference":"Book interior/page-001.png"}}""");

        Assert.True(response.Ok);
        Assert.Equal("book.asset.preview", response.Command);
        Assert.Same(preview, response.Payload);
        Assert.Equal(("Book One", "Book interior/page-001.png"), service.LastRequest);
    }

    [Fact]
    public async Task AssetPreviewRejectsMissingOrUnknownAssets()
    {
        var response = await new WebViewBridgeRouter(assetPreviewService: new StubAssetPreviewService(null))
            .HandleAsync("""{"version":1,"id":"preview-missing","command":"book.asset.preview.get","payload":{"bookId":"Book One","sourceReference":"outside.png"}}""");

        Assert.False(response.Ok);
        Assert.Equal("asset_preview_not_found", response.Error);
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
        new(snapshots, new NoopRecoveryService());

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

    private sealed class StubAssetPreviewService(BookAssetPreview? preview) : IBookAssetPreviewService
    {
        public (string BookId, string SourceReference)? LastRequest { get; private set; }
        public ValueTask<BookAssetPreview?> GetAsync(string bookId, string sourceReference, CancellationToken cancellationToken = default)
        {
            LastRequest = (bookId, sourceReference);
            return ValueTask.FromResult(preview);
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
        public ValueTask SelectAsync(string bookId, string coverReference, CancellationToken cancellationToken = default)
        {
            LastSelection = (bookId, coverReference);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubInteriorFrameModeService : IInteriorFrameModeService
    {
        public (string BookId, string SourceReference, FrameMode Mode)? LastSelection { get; private set; }

        public ValueTask SetAsync(string bookId, string sourceReference, FrameMode mode, CancellationToken cancellationToken = default)
        {
            LastSelection = (bookId, sourceReference, mode);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingInteriorFrameModeService : IInteriorFrameModeService
    {
        public ValueTask SetAsync(string bookId, string sourceReference, FrameMode mode, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidDataException("The workspace state is unavailable."));
    }

    private static ApplicationSnapshot CreateSnapshot() => new(
        new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], []),
        GlobalSettings.Default,
        [],
        DateTimeOffset.UnixEpoch);
}
