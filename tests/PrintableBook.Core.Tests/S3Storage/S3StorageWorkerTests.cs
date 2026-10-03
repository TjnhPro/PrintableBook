using System.Security.Cryptography;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.S3Storage;

public sealed class S3StorageWorkerTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.S3Worker.{Guid.NewGuid():N}");

    [Fact]
    public async Task Upload_preflights_all_files_and_uploads_only_missing_or_changed_files()
    {
        const string bookId = "Book One";
        var output = await CreateOutputAsync(bookId);
        var session = new FakeObjectSession();
        var files = S3StoragePolicy.FileNames(bookId);
        session.Remote[Key(files[0])] = await RemoteAsync(Path.Combine(output, files[0]));
        session.Remote[Key(files[1])] = new("outdated", 1, true);
        var (worker, request) = CreateWorker(bookId, session, S3StorageAction.Upload);

        var result = Assert.IsType<S3StorageBookView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(request, new Context(), CancellationToken.None));

        Assert.Equal(S3StorageOutcome.Completed, result.Outcome);
        Assert.Equal(S3StorageFileState.Skipped, result.Files[0].State);
        Assert.Equal(S3StorageFileState.Uploaded, result.Files[1].State);
        Assert.Equal(6, session.Uploaded.Count);
        Assert.Equal(7, result.CompletedCount);
        Assert.Equal(6, result.UploadCompletedCount);
        Assert.InRange(session.MaximumActive, 1, S3StoragePolicy.MaximumFileConcurrency);
        Assert.NotNull(result.LastCheckedAtUtc);
        Assert.NotNull(result.LastUploadedAtUtc);
    }

    [Fact]
    public async Task Check_never_uploads_and_reports_remote_differences()
    {
        const string bookId = "Book Two";
        await CreateOutputAsync(bookId);
        var session = new FakeObjectSession();
        session.Remote[Key(S3StoragePolicy.FileNames(bookId)[0])] = new("outdated", 1, true);
        var (worker, request) = CreateWorker(bookId, session, S3StorageAction.Check);

        var result = Assert.IsType<S3StorageBookView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(request, new Context(), CancellationToken.None));

        Assert.Equal(S3StorageFileState.Changed, result.Files[0].State);
        Assert.All(result.Files.Skip(1), file => Assert.Equal(S3StorageFileState.MissingRemote, file.State));
        Assert.Empty(session.Uploaded);
        Assert.Null(result.LastUploadedAtUtc);
    }

    [Fact]
    public async Task Upload_with_an_incomplete_manifest_performs_zero_puts()
    {
        const string bookId = "Book Three";
        await CreateOutputAsync(bookId);
        File.Delete(Path.Combine(root, "sources", bookId, "Output", S3StoragePolicy.FileNames(bookId)[3]));
        var session = new FakeObjectSession();
        var (worker, request) = CreateWorker(bookId, session, S3StorageAction.Upload);

        var result = Assert.IsType<S3StorageBookView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(request, new Context(), CancellationToken.None));

        Assert.Equal(S3StorageOutcome.CompletedWithErrors, result.Outcome);
        Assert.Equal("s3_upload_preflight_failed", result.WarningCode);
        Assert.Contains(result.Files, file => file.State == S3StorageFileState.MissingLocal);
        Assert.Empty(session.Uploaded);
    }

    [Fact]
    public async Task Shared_file_gate_reaches_four_workers_and_never_exceeds_four()
    {
        const string bookId = "Book Four";
        await CreateOutputAsync(bookId);
        var session = new FakeObjectSession(TimeSpan.FromMilliseconds(40));
        foreach (var fileName in S3StoragePolicy.FileNames(bookId))
            session.Remote[Key(fileName)] = new("outdated", 1, true, 1);
        var (worker, request) = CreateWorker(bookId, session, S3StorageAction.Check);

        await ((IBackgroundTaskWorker)worker).ExecuteAsync(request, new Context(), CancellationToken.None);

        Assert.Equal(S3StoragePolicy.MaximumFileConcurrency, session.MaximumActive);
    }

    [Fact]
    public async Task Check_lists_once_and_preserves_remote_facts_for_a_missing_local_file()
    {
        const string bookId = "Book Five";
        var output = await CreateOutputAsync(bookId);
        var fileName = S3StoragePolicy.FileNames(bookId)[0];
        var path = Path.Combine(output, fileName);
        var remote = await RemoteAsync(path);
        File.Delete(path);
        var session = new FakeObjectSession();
        session.Remote[Key(fileName)] = remote;
        var (worker, request) = CreateWorker(bookId, session, S3StorageAction.Check);

        var result = Assert.IsType<S3StorageBookView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(request, new Context(), CancellationToken.None));

        Assert.Equal(1, session.ListCount);
        Assert.Equal(1, session.HeadCount);
        Assert.Equal(S3StorageFileState.MissingLocal, result.Files[0].State);
        Assert.False(result.Files[0].LocalExists);
        Assert.True(result.Files[0].RemoteExists);
        Assert.Equal(remote.Length, result.Files[0].RemoteLength);
        Assert.Equal(remote.Sha256, result.Files[0].RemoteSha256);
    }

    [Fact]
    public async Task Check_treats_missing_remote_length_metadata_as_changed()
    {
        const string bookId = "Book Six";
        var output = await CreateOutputAsync(bookId);
        var fileName = S3StoragePolicy.FileNames(bookId)[0];
        var remote = await RemoteAsync(Path.Combine(output, fileName));
        var session = new FakeObjectSession();
        session.Remote[Key(fileName)] = remote with { MetadataLength = null };
        var (worker, request) = CreateWorker(bookId, session, S3StorageAction.Check);

        var result = Assert.IsType<S3StorageBookView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(request, new Context(), CancellationToken.None));

        Assert.Equal(S3StorageFileState.Changed, result.Files[0].State);
    }

    [Fact]
    public async Task Receipt_checkpoints_are_secret_free_and_do_not_persist_derived_public_urls()
    {
        const string bookId = "Book Seven";
        await CreateOutputAsync(bookId);
        var session = new FakeObjectSession();
        var receipts = new ReceiptStore();
        var (worker, request) = CreateWorker(bookId, session, S3StorageAction.Check, receipts);

        await ((IBackgroundTaskWorker)worker).ExecuteAsync(request, new Context(), CancellationToken.None);

        Assert.True(receipts.Saved.Count >= 9);
        Assert.All(receipts.Saved, receipt => Assert.All(receipt.View.Files, row => Assert.Empty(row.PublicUrl)));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
    }

    private (S3StorageWorker Worker, S3StorageTaskRequest Request) CreateWorker(string bookId, FakeObjectSession session, S3StorageAction action, ReceiptStore? receiptStore = null)
    {
        var contexts = new S3StorageOperationContextStore();
        var settings = new S3StorageSettings("access", "secret", new("us-east-1", "valid-bucket", "coloring"));
        var operation = contexts.Create(settings, Guid.NewGuid());
        var worker = new S3StorageWorker(
            new SnapshotProvider(CreateSnapshot(bookId, "B0FCC8Q6JP")),
            contexts,
            new FakeObjectSessionFactory(session),
            new BookOutputLeaseCoordinator(),
            receiptStore ?? new ReceiptStore());
        return (worker, new(bookId, action, operation.Id));
    }

    private async Task<string> CreateOutputAsync(string bookId)
    {
        var output = Path.Combine(root, "sources", bookId, "Output");
        Directory.CreateDirectory(output);
        foreach (var fileName in S3StoragePolicy.FileNames(bookId)) await File.WriteAllTextAsync(Path.Combine(output, fileName), fileName);
        return output;
    }

    private static string Key(string fileName) => S3StoragePolicy.ObjectKey("coloring", "B0FCC8Q6JP", fileName);

    private static async Task<S3RemoteObject> RemoteAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return new(Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant(), stream.Length, true, stream.Length);
    }

    private ApplicationSnapshot CreateSnapshot(string bookId, string asin)
    {
        var rootReference = new DirectoryReference(root);
        var bookDirectory = new DirectoryReference(Path.Combine(root, "sources", bookId));
        var id = new BookId(bookId);
        var workspace = new BookWorkspace(id, new(Path.Combine(bookDirectory.Value, ".printable-book")), new(Path.Combine(bookDirectory.Value, ".printable-book", "processed")), new(Path.Combine(bookDirectory.Value, ".printable-book", "output-temp")));
        var discovery = new ApplicationDiscovery(
            new(rootReference, new(Path.Combine(root, "brands")), new(Path.Combine(root, "sources")), new(Path.Combine(root, "settings.json"))),
            [],
            [new DiscoveredBook(bookId, id, bookDirectory, workspace)]);
        var summary = new BookDesktopSummary(
            id, "Ready", [], BookProcessingStatus.Completed, null, null, [], [], [], 0,
            Metadata: BookProductionMetadata.Create(bookId, null, null, null, null, asin));
        return new(discovery, GlobalSettings.Default, [summary], DateTimeOffset.UtcNow);
    }

    private sealed class SnapshotProvider(ApplicationSnapshot snapshot) : IApplicationSnapshotProvider
    {
        public ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

    private sealed class FakeObjectSessionFactory(FakeObjectSession session) : IS3ObjectSessionFactory
    {
        public ValueTask<IS3ObjectSession> OpenAsync(S3StorageSettings settings, CancellationToken cancellationToken = default) => ValueTask.FromResult<IS3ObjectSession>(session);
    }

    private sealed class FakeObjectSession(TimeSpan? delay = null) : IS3ObjectSession
    {
        private int active;
        public Dictionary<string, S3RemoteObject> Remote { get; } = new(StringComparer.Ordinal);
        public List<string> Uploaded { get; } = [];
        public int MaximumActive { get; private set; }
        public int ListCount { get; private set; }
        public int HeadCount => Volatile.Read(ref headCount);

        public ValueTask<S3RemotePrefix> ListPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCount++;
            IReadOnlySet<string> keys = Remote.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
            return ValueTask.FromResult(new S3RemotePrefix(keys));
        }

        public async ValueTask<S3RemoteObject?> HeadAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                Interlocked.Increment(ref headCount);
                if (delay is { } latency) await Task.Delay(latency, cancellationToken);
                return Remote.TryGetValue(objectKey, out var value) ? value : null;
            }
            finally { Interlocked.Decrement(ref active); }
        }

        public async ValueTask PutAsync(string objectKey, FileReference source, string contentType, string sha256, long length, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                if (delay is { } latency) await Task.Delay(latency, cancellationToken);
                lock (Uploaded) Uploaded.Add(objectKey);
                Remote[objectKey] = new(sha256, length, true, length);
            }
            finally { Interlocked.Decrement(ref active); }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Enter()
        {
            var current = Interlocked.Increment(ref active);
            lock (this) MaximumActive = Math.Max(MaximumActive, current);
        }

        private int headCount;
    }

    private sealed class ReceiptStore : IS3PublicationReceiptStore
    {
        public List<S3PublicationReceipt> Saved { get; } = [];
        public ValueTask<S3PublicationReceiptLoadResult> LoadAsync(DirectoryReference bookDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(new S3PublicationReceiptLoadResult(null));
        public ValueTask SaveAsync(DirectoryReference bookDirectory, S3PublicationReceipt receipt, CancellationToken cancellationToken = default)
        {
            Saved.Add(receipt);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Context : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("task-storage-test");
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class { }
    }
}
