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
    public async Task RefreshAsync_marks_a_book_invalid_when_discovery_only_found_unsupported_interior_files()
    {
        var snapshot = await new ApplicationSnapshotService(
            new StubDiscovery(), new StubSettingsStore(), new UnsupportedInteriorScanner(), new StubStateStore(), new StubFileSystem()).RefreshAsync();

        var summary = Assert.Single(snapshot.BookSummaries);
        Assert.Equal("Invalid", summary.ValidationStatus);
        Assert.Contains(summary.ValidationChecks, check => check.Code == "book.interior_empty" && !check.IsSuccess);
        Assert.Empty(summary.Assets!);
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
    public async Task RefreshAsync_exposes_a_canonical_file_url_for_each_local_asset()
    {
        var sourceReference = Path.Combine("sources", "Book A", "Book interior", "Bộ sách #1 %", "page 001.png");
        var snapshot = await new ApplicationSnapshotService(
            new StubDiscovery(),
            new StubSettingsStore(),
            new DirectReferenceScanner(sourceReference),
            new StubStateStore(),
            new StubFileSystem()).RefreshAsync();

        var asset = Assert.Single(Assert.Single(snapshot.BookSummaries).Assets!);

        Assert.Equal(sourceReference, asset.SourceReference);
        Assert.Equal(new Uri(Path.GetFullPath(sourceReference)).AbsoluteUri, asset.LocalImageUrl);
        Assert.StartsWith("file:", asset.LocalImageUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAsync_keeps_asset_dimensions_unavailable_without_inspecting_source_images()
    {
        var snapshot = await new ApplicationSnapshotService(
            new StubDiscovery(),
            new StubSettingsStore(),
            new DirectReferenceScanner(Path.Combine("sources", "Book A", "Book interior", "page-001.png")),
            new StubStateStore(),
            new StubFileSystem()).RefreshAsync();

        var asset = Assert.Single(Assert.Single(snapshot.BookSummaries).Assets!);
        Assert.Null(asset.Width);
        Assert.Null(asset.Height);
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

    [Fact]
    public async Task RefreshAsync_exposes_background_choice_active_sources_and_zero_active_validation()
    {
        var state = BookProcessingState.NotStarted(new BookId("Book A")).SetHasBackground(true).SetInteriorActive("page-1.png", false);
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new StubScanner(), new StubStateStore(explicitState: state), new StubFileSystem()).RefreshAsync();
        var summary = Assert.Single(snapshot.BookSummaries);
        Assert.True(summary.HasBackground);
        Assert.Equal(0, summary.ActiveInteriorSourcePageCount);
        Assert.False(Assert.Single(summary.InteriorSourcePages!).IsActive);
        Assert.False(Assert.Single(summary.Assets!, asset => asset.Kind == "Interior").IsActive);
        Assert.Equal("Invalid", summary.ValidationStatus);
        Assert.Contains(summary.ValidationChecks, check => check.Code == "book.no_active_interior_pages");
    }

    [Fact]
    public async Task RefreshAsync_marks_an_empty_custom_intro_selection_as_needing_review()
    {
        var state = BookProcessingState.NotStarted(new BookId("Book A")).SetHasIntro(true).SetIntroTemplateKeys([]);
        var snapshot = await new ApplicationSnapshotService(new StubDiscovery(), new StubSettingsStore(), new StubScanner(), new StubStateStore(explicitState: state), new StubFileSystem()).RefreshAsync();

        var summary = Assert.Single(snapshot.BookSummaries);
        Assert.True(summary.HasIntro);
        Assert.Empty(summary.SelectedIntroTemplateKeys!);
        Assert.Equal("Invalid", summary.ValidationStatus);
        Assert.Contains(summary.ValidationChecks, check => check.Code == "book.intro_selection_required" && !check.IsSuccess);
    }

    [Fact]
    public async Task RefreshAsync_builds_book_summaries_with_concurrency_limited_to_four_and_keeps_discovery_order()
    {
        var scanner = new GatedScanner();
        var refresh = new ApplicationSnapshotService(
            new ManyBookDiscovery(8), new StubSettingsStore(), scanner, new StubStateStore(), new StubFileSystem())
            .RefreshAsync()
            .AsTask();

        await scanner.FourScansEntered.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(4, scanner.Started);
        Assert.Equal(4, scanner.MaximumConcurrentScans);

        scanner.ReleaseAllButFirstBook();
        await scanner.LaterBookStarted.WaitAsync(TimeSpan.FromSeconds(2));
        scanner.ReleaseFirstBook();
        var snapshot = await refresh;

        Assert.Equal(Enumerable.Range(1, 8).Select(number => $"Book {number:D3}"), snapshot.BookSummaries.Select(summary => summary.BookId.Value));
        Assert.InRange(scanner.MaximumConcurrentScans, 1, 4);
    }

    [Fact]
    public async Task RefreshAsync_propagates_a_book_summary_failure()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new ApplicationSnapshotService(
            new ManyBookDiscovery(2), new StubSettingsStore(), new ThrowingScanner(), new StubStateStore(), new StubFileSystem())
            .RefreshAsync()
            .AsTask());

        Assert.Equal("scan failed", exception.Message);
    }

    [Fact]
    public async Task RefreshAsync_honors_cancellation_while_building_book_summaries()
    {
        var scanner = new GatedScanner();
        using var cancellation = new CancellationTokenSource();
        var refresh = new ApplicationSnapshotService(
            new ManyBookDiscovery(8), new StubSettingsStore(), scanner, new StubStateStore(), new StubFileSystem())
            .RefreshAsync(cancellation.Token)
            .AsTask();

        await scanner.FourScansEntered.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
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

    private sealed class ManyBookDiscovery(int count) : IApplicationRootDiscovery
    {
        public ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            var paths = new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json"));
            var books = Enumerable.Range(1, count)
                .Select(number =>
                {
                    var id = new BookId($"Book {number:D3}");
                    return new DiscoveredBook(id.Value, id, new DirectoryReference($"sources/{id.Value}"), new BookWorkspace(id, new DirectoryReference($"work/{id.Value}"), new DirectoryReference($"processed/{id.Value}"), new DirectoryReference($"temp/{id.Value}")));
                })
                .ToArray();
            return ValueTask.FromResult(new ApplicationDiscovery(paths, [], books));
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

    private sealed class GatedScanner : IBookSourceScanner
    {
        private readonly TaskCompletionSource fourScansEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstBook = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource laterBookStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeScans;
        private int started;

        public Task FourScansEntered => fourScansEntered.Task;
        public Task LaterBookStarted => laterBookStarted.Task;
        public int Started => Volatile.Read(ref started);
        public int MaximumConcurrentScans { get; private set; }

        public async ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default)
        {
            var scanNumber = Interlocked.Increment(ref started);
            if (scanNumber >= 5) laterBookStarted.TrySetResult();
            var active = Interlocked.Increment(ref activeScans);
            MaximumConcurrentScans = Math.Max(MaximumConcurrentScans, active);
            if (active == 4) fourScansEntered.TrySetResult();
            try
            {
                await release.Task.WaitAsync(cancellationToken);
                if (string.Equals(bookId.Value, "Book 001", StringComparison.Ordinal))
                {
                    await releaseFirstBook.Task.WaitAsync(cancellationToken);
                }
                return BookSourceScanResult.Succeeded(new BookSource([
                    new BookAsset(Path.Combine(bookDirectory.Value, "Book interior", "page-1.png"), BookAssetKind.Interior)]));
            }
            finally
            {
                Interlocked.Decrement(ref activeScans);
            }
        }

        public void ReleaseAllButFirstBook() => release.TrySetResult();
        public void ReleaseFirstBook() => releaseFirstBook.TrySetResult();
    }

    private sealed class ThrowingScanner : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<BookSourceScanResult>(new InvalidOperationException("scan failed"));
    }

    private sealed class DirectReferenceScanner(string sourceReference) : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(new BookSource([
                new BookAsset(sourceReference, BookAssetKind.Interior)])));
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

    private sealed class UnsupportedInteriorScanner : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(new BookSource([
                new BookAsset("notes.txt", BookAssetKind.Interior)])));
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

    private sealed class StubStateStore(string? selectedCoverReference = null, BookProcessingState? explicitState = null) : IBookWorkspaceStateStore
    {
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BookProcessingState?>(explicitState ?? (selectedCoverReference is null
                ? null
                : BookProcessingState.NotStarted(workspace.BookId).SelectCover(selectedCoverReference)));
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
