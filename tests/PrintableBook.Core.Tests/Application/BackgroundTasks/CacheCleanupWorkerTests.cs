using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application.BackgroundTasks;

public sealed class CacheCleanupWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_cleans_only_completed_books_with_all_recorded_outputs()
    {
        var book = CreateBook("completed");
        var state = Completed(book, "output.pdf");
        var storage = new StubStorage { BytesByBook = { [book.Id.Value] = 42 } };

        var result = await ExecuteAsync([book], [state], ["output.pdf"], storage);

        Assert.Equal((1, 1, 0, 0, 42L), (result.ScannedBooks, result.CleanedBooks, result.SkippedBooks, result.FailedBooks, result.FreedBytes));
        Assert.Equal("Cleaned", Assert.Single(result.Books).Status);
    }

    [Fact]
    public async Task ExecuteAsync_clears_published_preview_manifest_when_rendered_pages_are_removed()
    {
        var book = CreateBook("completed");
        var state = Completed(book, "output.pdf")
            .RecordPublishedInteriorPreviews([new PublishedInteriorPreview("page-0001", "processed/interior/page-0001.png")]);
        var stateStore = new StubStateStore([state]);
        var worker = new CacheCleanupWorker(
            new StubDiscovery([book]), stateStore, new StubFileSystem(["output.pdf"]), new StubStorage());

        var result = Assert.IsType<CacheCleanupResult>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(new CacheCleanupRequest(), new StubContext(), CancellationToken.None));

        Assert.Equal("Cleaned", Assert.Single(result.Books).Status);
        Assert.Empty((await stateStore.LoadAsync(book.Workspace))!.PublishedInteriorPreviews!);
    }

    [Fact]
    public async Task ExecuteAsync_skips_completed_book_when_output_is_missing()
    {
        var book = CreateBook("missing");

        var result = await ExecuteAsync([book], [Completed(book, "missing.pdf")], [], new StubStorage());

        var outcome = Assert.Single(result.Books);
        Assert.Equal("Skipped", outcome.Status);
        Assert.Equal("Published output is missing.", outcome.Reason);
    }

    [Theory]
    [InlineData(BookProcessingStatus.NotStarted)]
    [InlineData(BookProcessingStatus.Running)]
    [InlineData(BookProcessingStatus.Failed)]
    [InlineData(BookProcessingStatus.Cancelled)]
    [InlineData(BookProcessingStatus.Interrupted)]
    public async Task ExecuteAsync_skips_non_completed_workspace_states(BookProcessingStatus status)
    {
        var book = CreateBook(status.ToString());
        var state = BookProcessingState.NotStarted(book.Id) with { Status = status, PublishedArtifactReferences = ["output.pdf"] };

        var result = await ExecuteAsync([book], [state], ["output.pdf"], new StubStorage());

        Assert.Equal("Skipped", Assert.Single(result.Books).Status);
        Assert.Equal($"Workspace status is {status}.", result.Books[0].Reason);
    }

    [Fact]
    public async Task ExecuteAsync_skips_completed_book_without_recorded_output()
    {
        var book = CreateBook("no-output");
        var state = BookProcessingState.NotStarted(book.Id).Complete(DateTimeOffset.UtcNow);

        var result = await ExecuteAsync([book], [state], [], new StubStorage());

        Assert.Equal("No published output is recorded.", Assert.Single(result.Books).Reason);
    }

    [Fact]
    public async Task ExecuteAsync_continues_after_one_book_cleanup_fails()
    {
        var failed = CreateBook("failed");
        var cleaned = CreateBook("cleaned");
        var storage = new StubStorage { Failures = { [failed.Id.Value] = new IOException("locked") }, BytesByBook = { [cleaned.Id.Value] = 9 } };

        var result = await ExecuteAsync([failed, cleaned], [Completed(failed, "failed.pdf"), Completed(cleaned, "cleaned.pdf")], ["failed.pdf", "cleaned.pdf"], storage);

        Assert.Equal((1, 1, 9L), (result.CleanedBooks, result.FailedBooks, result.FreedBytes));
        Assert.Equal(["Failed", "Cleaned"], result.Books.Select(item => item.Status));
    }

    [Fact]
    public async Task ExecuteAsync_reports_actual_partial_freed_bytes_from_storage_failure()
    {
        var book = CreateBook("partial");
        var storage = new StubStorage { Failures = { [book.Id.Value] = new BookStorageCleanupException(13, new IOException("locked")) } };

        var result = await ExecuteAsync([book], [Completed(book, "output.pdf")], ["output.pdf"], storage);

        Assert.Equal(13, result.FreedBytes);
        Assert.Equal(13, Assert.Single(result.Books).FreedBytes);
    }

    [Fact]
    public async Task ExecuteAsync_processes_books_sequentially()
    {
        var first = CreateBook("first");
        var second = CreateBook("second");
        var storage = new StubStorage { BytesByBook = { [first.Id.Value] = 1, [second.Id.Value] = 2 } };

        await ExecuteAsync([first, second], [Completed(first, "first.pdf"), Completed(second, "second.pdf")], ["first.pdf", "second.pdf"], storage);

        Assert.Equal(1, storage.MaximumConcurrentCalls);
        Assert.Equal([first.Id.Value, second.Id.Value], storage.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExecuteAsync([], [], [], new StubStorage(), cancellation.Token).AsTask());
    }

    private static async ValueTask<CacheCleanupResult> ExecuteAsync(
        IReadOnlyList<DiscoveredBook> books,
        IReadOnlyList<BookProcessingState> states,
        IReadOnlyList<string> files,
        StubStorage storage,
        CancellationToken cancellationToken = default)
    {
        var worker = new CacheCleanupWorker(new StubDiscovery(books), new StubStateStore(states), new StubFileSystem(files), storage);
        var result = await ((IBackgroundTaskWorker)worker).ExecuteAsync(
            new CacheCleanupRequest(),
            new StubContext(),
            cancellationToken);

        return Assert.IsType<CacheCleanupResult>(result);
    }

    private static DiscoveredBook CreateBook(string id)
    {
        var bookId = new BookId(id);
        return new DiscoveredBook(id, bookId, new DirectoryReference($"sources/{id}"), new BookWorkspace(bookId, new DirectoryReference($"sources/{id}/.workspace"), new DirectoryReference($"sources/{id}/.workspace/processed"), new DirectoryReference($"sources/{id}/.workspace/output-temp")));
    }

    private static BookProcessingState Completed(DiscoveredBook book, string artifact) =>
        BookProcessingState.NotStarted(book.Id).RecordPublishedArtifacts([artifact]).Complete(DateTimeOffset.UtcNow);

    private sealed class StubDiscovery(IReadOnlyList<DiscoveredBook> books) : IApplicationRootDiscovery
    {
        public ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], books));
        }
    }

    private sealed class StubStateStore : IBookWorkspaceStateStore
    {
        private readonly Dictionary<BookId, BookProcessingState> states;

        public StubStateStore(IReadOnlyList<BookProcessingState> states) => this.states = states.ToDictionary(state => state.BookId);

        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BookProcessingState?>(states.GetValueOrDefault(workspace.BookId));

        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default)
        {
            states[workspace.BookId] = state;
            return ValueTask.CompletedTask;
        }
        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BookProcessingLogEntry>>([]);
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubFileSystem(IReadOnlyList<string> existing) : IFileSystem
    {
        public ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult(existing.Contains(file.Value, StringComparer.Ordinal));
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

    private sealed class StubStorage : IBookStorageMaintenance
    {
        private int activeCalls;
        public Dictionary<string, long> BytesByBook { get; } = [];
        public Dictionary<string, Exception> Failures { get; } = [];
        public List<string> Calls { get; } = [];
        public int MaximumConcurrentCalls { get; private set; }
        public async ValueTask<long> ClearHeavyProcessingCacheAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref activeCalls);
            MaximumConcurrentCalls = Math.Max(MaximumConcurrentCalls, active);
            Calls.Add(workspace.BookId.Value);
            try
            {
                await Task.Yield();
                if (Failures.TryGetValue(workspace.BookId.Value, out var failure)) throw failure;
                return BytesByBook.GetValueOrDefault(workspace.BookId.Value);
            }
            finally { Interlocked.Decrement(ref activeCalls); }
        }
    }

    private sealed class StubContext : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("cleanup");
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class { }
    }
}
