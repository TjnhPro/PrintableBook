using PrintableBook.Desktop.Bridge;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;

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
            DateTimeOffset.UnixEpoch);
        var router = new WebViewBridgeRouter(new StubSnapshotService(snapshot));

        var response = await router.HandleAsync("""{"version":1,"id":"request-4","command":"app.refresh"}""");

        Assert.True(response.Ok);
        Assert.Equal("app.snapshot", response.Command);
        Assert.Same(snapshot, response.Payload);
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

    private sealed class StubSnapshotService(ApplicationSnapshot snapshot) : IApplicationSnapshotService
    {
        public ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
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
}
