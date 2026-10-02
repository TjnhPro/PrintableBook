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
    public async Task Upload_preflights_and_uploads_only_missing_or_changed_files()
    {
        const string bookId = "Book One";
        var output = Path.Combine(root, "sources", bookId, "Output");
        Directory.CreateDirectory(output);
        foreach (var fileName in S3StoragePolicy.FileNames(bookId)) await File.WriteAllTextAsync(Path.Combine(output, fileName), fileName);

        var client = new FakeObjectClient();
        var firstName = S3StoragePolicy.FileNames(bookId)[0];
        var firstPath = Path.Combine(output, firstName);
        client.Remote[S3StoragePolicy.ObjectKey("B0FCC8Q6JP", firstName)] = new(await HashAsync(firstPath));
        var secondName = S3StoragePolicy.FileNames(bookId)[1];
        client.Remote[S3StoragePolicy.ObjectKey("B0FCC8Q6JP", secondName)] = new("outdated");
        var context = new Context();
        var worker = new S3StorageWorker(new SnapshotProvider(CreateSnapshot(bookId, "B0FCC8Q6JP")), new SettingsStore(), client);

        var result = Assert.IsType<S3StorageBookView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(
            new S3StorageTaskRequest(bookId, S3StorageAction.Upload), context, CancellationToken.None));

        Assert.Equal(S3StorageOutcome.Completed, result.Outcome);
        Assert.Equal(S3StorageFileState.Skipped, result.Files[0].State);
        Assert.Equal(S3StorageFileState.Uploaded, result.Files[1].State);
        Assert.Equal(6, client.Uploaded.Count);
        Assert.Equal(7, result.CompletedCount);
        Assert.NotNull(result.LastCheckedAtUtc);
        Assert.NotNull(result.LastUploadedAtUtc);
    }

    [Fact]
    public async Task Check_never_uploads_and_reports_remote_differences()
    {
        const string bookId = "Book Two";
        var output = Path.Combine(root, "sources", bookId, "Output");
        Directory.CreateDirectory(output);
        foreach (var fileName in S3StoragePolicy.FileNames(bookId)) await File.WriteAllTextAsync(Path.Combine(output, fileName), fileName);
        var client = new FakeObjectClient();
        var changed = S3StoragePolicy.FileNames(bookId)[0];
        client.Remote[S3StoragePolicy.ObjectKey("B0FCC8Q6JP", changed)] = new("outdated");
        var worker = new S3StorageWorker(new SnapshotProvider(CreateSnapshot(bookId, "B0FCC8Q6JP")), new SettingsStore(), client);

        var result = Assert.IsType<S3StorageBookView>(await ((IBackgroundTaskWorker)worker).ExecuteAsync(
            new S3StorageTaskRequest(bookId, S3StorageAction.Check), new Context(), CancellationToken.None));

        Assert.Equal(S3StorageFileState.Changed, result.Files[0].State);
        Assert.All(result.Files.Skip(1), file => Assert.Equal(S3StorageFileState.Missing, file.State));
        Assert.Empty(client.Uploaded);
        Assert.Null(result.LastUploadedAtUtc);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
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
            Metadata: BookProductionMetadata.Create("Book One", null, null, null, null, asin));
        return new(discovery, GlobalSettings.Default, [summary], DateTimeOffset.UtcNow);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private sealed class SnapshotProvider(ApplicationSnapshot snapshot) : IApplicationSnapshotProvider
    {
        public ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

    private sealed class SettingsStore : IS3StorageSettingsStore
    {
        public ValueTask<S3StorageSettings?> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<S3StorageSettings?>(new("access", "secret", "valid-bucket", "us-east-1", null));
        public ValueTask<S3StorageConfiguration> SaveAsync(S3StorageSettingsInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeObjectClient : IS3ObjectClient
    {
        public Dictionary<string, S3RemoteObject> Remote { get; } = new(StringComparer.Ordinal);
        public List<string> Uploaded { get; } = [];

        public ValueTask<S3RemoteObject?> GetAsync(S3StorageSettings settings, string objectKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Remote.TryGetValue(objectKey, out var value) ? value : null);

        public ValueTask UploadAsync(S3StorageSettings settings, string objectKey, FileReference source, string sha256, CancellationToken cancellationToken = default)
        {
            Uploaded.Add(objectKey);
            Remote[objectKey] = new(sha256);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Context : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("task-storage-test");
        public object? View { get; private set; }
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class => View = view;
    }
}
