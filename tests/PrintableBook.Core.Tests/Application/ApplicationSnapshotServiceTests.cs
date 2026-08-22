using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application;

public sealed class ApplicationSnapshotServiceTests
{
    [Fact]
    public async Task RefreshAsync_returns_one_coherent_discovery_snapshot()
    {
        var discovery = new StubDiscovery();
        var settings = new StubSettingsStore();
        var snapshot = await new ApplicationSnapshotService(discovery, settings, new StubScanner(), new StubStateStore()).RefreshAsync();

        Assert.Equal("Brand A", Assert.Single(snapshot.Discovery.Brands).Name);
        Assert.Equal("Book A", Assert.Single(snapshot.Discovery.Books).Name);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(GlobalSettings.Default, snapshot.GlobalSettings);
        Assert.Equal(1, settings.LoadCallCount);
        Assert.Equal("Ready", Assert.Single(snapshot.BookSummaries).ValidationStatus);
    }

    private sealed class StubDiscovery : IApplicationRootDiscovery
    {
        public int CallCount { get; private set; }
        public ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            var paths = new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json"));
            var id = new BookId("Book A");
            return ValueTask.FromResult(new ApplicationDiscovery(paths, [new DiscoveredBrand("Brand A", new DirectoryReference("brands/Brand A"))], [new DiscoveredBook("Book A", id, new DirectoryReference("sources/Book A"), new BookWorkspace(id, new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("temp")))]));
        }
    }

    private sealed class StubSettingsStore : IGlobalSettingsStore
    {
        public int LoadCallCount { get; private set; }
        public ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCallCount++;
            return ValueTask.FromResult(GlobalSettings.Default);
        }

        public ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default)
        {
            LoadCallCount++;
            return ValueTask.FromResult(GlobalSettings.Default);
        }

        public ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubScanner : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(new BookSource([
                new BookAsset("cover.png", BookAssetKind.Cover),
                new BookAsset("page-1.png", BookAssetKind.Interior)])));
    }

    private sealed class StubStateStore : IBookWorkspaceStateStore
    {
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<BookProcessingState?>(null);
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
