using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Application.Diagnostics;
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
        var snapshot = await new ApplicationSnapshotService(discovery, settings, new StubScanner(), new StubStateStore(), new StubFileSystem()).RefreshAsync();

        Assert.Equal("Brand A", Assert.Single(snapshot.Discovery.Brands).Name);
        Assert.Equal("Book A", Assert.Single(snapshot.Discovery.Books).Name);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(GlobalSettings.Default, snapshot.GlobalSettings);
        Assert.Equal(1, settings.LoadCallCount);
        Assert.Equal("Ready", Assert.Single(snapshot.BookSummaries).ValidationStatus);
    }

    [Fact]
    public async Task RefreshAsync_allows_multiple_cover_candidates_for_interior_only_processing()
    {
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new MultipleCoverScanner(), new StubStateStore(), new StubFileSystem()).RefreshAsync();

        var summary = Assert.Single(snapshot.BookSummaries);
        Assert.Equal("Ready", summary.ValidationStatus);
        Assert.Equal(["cover-a.png", "cover-b.png"], summary.CoverCandidates);
        Assert.Contains(summary.ValidationChecks, check => check.Code == "book.cover_selection_optional" && check.IsSuccess && check.IsWarning);
    }

    [Fact]
    public async Task RefreshAsync_marks_an_interior_only_book_ready_and_reports_missing_cover_as_a_warning()
    {
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new InteriorOnlyScanner(), new StubStateStore(), new StubFileSystem()).RefreshAsync();

        var summary = Assert.Single(snapshot.BookSummaries);
        Assert.Equal("Ready", summary.ValidationStatus);
        Assert.Contains(summary.ValidationChecks, check => check.Code == "book.cover_skipped" && check.IsSuccess && check.IsWarning);
        Assert.Contains(summary.FullBookValidationChecks!, check => check.Code == "book.cover_required" && !check.IsSuccess);
    }

    [Fact]
    public async Task RefreshAsync_requires_an_explicit_cover_selection_for_full_book_validation_when_multiple_covers_exist()
    {
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new MultipleCoverScanner(), new StubStateStore(), new StubFileSystem()).RefreshAsync();

        var summary = Assert.Single(snapshot.BookSummaries);
        Assert.Contains(summary.FullBookValidationChecks!, check => check.Code == "book.cover_selection_required" && !check.IsSuccess);
    }

    [Fact]
    public async Task RefreshAsync_exposes_only_a_book_cover_asset_as_the_representative_preview()
    {
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new BookCoverScanner(), new StubStateStore(), new StubFileSystem()).RefreshAsync();

        Assert.EndsWith(Path.Combine("Book cover", "cover.png"), Assert.Single(snapshot.BookSummaries).RepresentativeCoverReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_uses_an_available_cover_folder_asset_when_book_cover_is_not_present()
    {
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new CoverFolderScanner(), new StubStateStore(), new StubFileSystem()).RefreshAsync();

        Assert.EndsWith(Path.Combine("Cover", "cover.png"), Assert.Single(snapshot.BookSummaries).RepresentativeCoverReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_prefers_the_cover_explicitly_selected_by_the_user_for_the_representative_preview()
    {
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new MultipleCoverScanner(), new StubStateStore("cover-b.png"), new StubFileSystem()).RefreshAsync();

        Assert.Equal("cover-b.png", Assert.Single(snapshot.BookSummaries).RepresentativeCoverReference);
    }

    [Fact]
    public async Task RefreshAsync_opens_sanitized_snapshot_operation_scopes()
    {
        var diagnostics = new RecordingDiagnostics();
        await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new StubScanner(), new StubStateStore(), new StubFileSystem(), diagnostics: diagnostics).RefreshAsync();

        Assert.Contains(("snapshot.refresh", null), diagnostics.Operations);
        Assert.Contains(("discovery", null), diagnostics.Operations);
        Assert.Contains(("book.scan", "Book A"), diagnostics.Operations);
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

    private sealed class RecordingDiagnostics : IOperationDiagnostics
    {
        public List<(string Operation, string? Subject)> Operations { get; } = [];
        public IDisposable Begin(string operation, string? subject = null)
        {
            Operations.Add((operation, subject));
            return new Scope();
        }

        public void Record(string operation, string? subject = null, string? detail = null) { }

        private sealed class Scope : IDisposable { public void Dispose() { } }
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

    private sealed class MultipleCoverScanner : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(new BookSource([
                new BookAsset("cover-a.png", BookAssetKind.Cover),
                new BookAsset("cover-b.png", BookAssetKind.Cover),
                new BookAsset("page-1.jpg", BookAssetKind.Interior)])));
    }

    private sealed class InteriorOnlyScanner : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(new BookSource([
                new BookAsset("page-1.jpg", BookAssetKind.Interior)])));
    }

    private sealed class BookCoverScanner : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(new BookSource([
                new BookAsset(Path.Combine(bookDirectory.Value, "Book cover", "cover.png"), BookAssetKind.Cover),
                new BookAsset(Path.Combine(bookDirectory.Value, "Book interior", "page-1.png"), BookAssetKind.Interior)])));
    }

    private sealed class CoverFolderScanner : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(new BookSource([
                new BookAsset(Path.Combine(bookDirectory.Value, "Cover", "cover.png"), BookAssetKind.Cover),
                new BookAsset(Path.Combine(bookDirectory.Value, "Interior", "page-1.png"), BookAssetKind.Interior)])));
    }

    private sealed class StubStateStore(string? selectedCoverReference = null) : IBookWorkspaceStateStore
    {
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BookProcessingState?>(selectedCoverReference is null
                ? null
                : BookProcessingState.NotStarted(workspace.BookId).SelectCover(selectedCoverReference));
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BookProcessingLogEntry>>([]);
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubFileSystem : IFileSystem
    {
        public ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask CreateDirectoryAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
        public async IAsyncEnumerable<FileReference> EnumerateFilesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
        public ValueTask<string> ReadTextAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult(string.Empty);
        public ValueTask WriteTextAtomicallyAsync(FileReference file, string content, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CopyFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask MoveFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DeleteFileAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DeleteDirectoryAsync(DirectoryReference directory, bool recursive, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
