using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;
using System.Text.Json;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PhysicalBookWorkspaceTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.WorkspaceTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateAsync_creates_the_agreed_workspace_layout_and_persists_recoverable_state()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "Book-One"));
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(new BookId("book-one"), bookDirectory);
        var store = new JsonBookWorkspaceStateStore(fileSystem);
        var state = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.Parse("2026-08-22T10:00:00Z"))
            .Fail("resize", new ProcessingFailure("image.resize_failed", "Invalid target."), DateTimeOffset.Parse("2026-08-22T10:01:00Z"));

        await store.SaveAsync(workspace, state);
        await store.AppendLogAsync(workspace, new BookProcessingLogEntry(DateTimeOffset.UtcNow, "stage.failed", "resize"));
        await store.SaveErrorAsync(workspace, state.Failure!);

        Assert.True(Directory.Exists(Path.Combine(bookDirectory.Value, ".workspace", "cache")));
        Assert.True(Directory.Exists(Path.Combine(bookDirectory.Value, ".workspace", "processed", "interior")));
        Assert.True(Directory.Exists(Path.Combine(bookDirectory.Value, ".workspace", "output-temp")));
        Assert.Equal(Path.Combine(bookDirectory.Value, ".workspace", "processed"), workspace.ProcessedDirectory.Value);
        Assert.Equal(Path.Combine(bookDirectory.Value, ".workspace", "output-temp"), workspace.TemporaryOutputDirectory.Value);
        var restored = await store.LoadAsync(workspace);
        Assert.Equal(BookProcessingStatus.Failed, restored!.Status);
        Assert.Equal("resize", restored.FailedStep);
        Assert.True(File.Exists(Path.Combine(bookDirectory.Value, ".workspace", "errors", "latest-error.json")));
    }

    [Fact]
    public async Task AppendLogAsync_writes_jsonl_that_LoadLogsAsync_can_round_trip()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "Book-Logs"));
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(new BookId("book-logs"), bookDirectory);
        var store = new JsonBookWorkspaceStateStore(fileSystem);
        var first = new BookProcessingLogEntry(
            DateTimeOffset.Parse("2026-08-22T10:00:00Z"),
            "step.completed",
            "publish");
        var second = new BookProcessingLogEntry(
            DateTimeOffset.Parse("2026-08-22T10:01:00Z"),
            "book.completed");

        await store.AppendLogAsync(workspace, first);
        await store.AppendLogAsync(workspace, second);

        var restored = await store.LoadLogsAsync(workspace);
        var logFile = Path.Combine(workspace.WorkingDirectory.Value, "logs", "processing.jsonl");
        var records = (await File.ReadAllLinesAsync(logFile))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        Assert.Equal([first, second], restored);
        Assert.Equal(2, records.Length);
        Assert.All(records, record => Assert.NotNull(JsonSerializer.Deserialize<BookProcessingLogEntry>(record, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    [Fact]
    public async Task LoadLogsAsync_reads_legacy_indented_json_records()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "Book-Legacy-Logs"));
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(new BookId("book-legacy-logs"), bookDirectory);
        var store = new JsonBookWorkspaceStateStore(fileSystem);
        var first = new BookProcessingLogEntry(DateTimeOffset.Parse("2026-08-22T10:00:00Z"), "book.started", null);
        var second = new BookProcessingLogEntry(DateTimeOffset.Parse("2026-08-22T10:01:00Z"), "step.completed", "publish");
        var logFile = Path.Combine(workspace.WorkingDirectory.Value, "logs", "processing.jsonl");
        var legacyOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

        await File.WriteAllTextAsync(
            logFile,
            JsonSerializer.Serialize(first, legacyOptions) + Environment.NewLine + JsonSerializer.Serialize(second, legacyOptions));

        var restored = await store.LoadLogsAsync(workspace);

        Assert.Equal([first, second], restored);
    }

    [Fact]
    public async Task Shuffle_store_round_trips_the_generated_page_order_without_renaming_sources()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "Book-Two"));
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(new BookId("book-two"), bookDirectory);
        var shuffleMap = InteriorShuffleIndexGenerator.Generate(
            [new FileReference("a.png"), new FileReference("b.png"), new FileReference("c.png")],
            seed: 73);
        var store = new JsonInteriorShuffleStore(fileSystem);

        await store.SaveAsync(workspace, shuffleMap);

        var restored = await store.LoadAsync(workspace);
        Assert.NotNull(restored);
        Assert.Equal(shuffleMap.Entries, restored.Entries);
        Assert.Equal(73, restored.Seed);
        Assert.True(File.Exists(Path.Combine(bookDirectory.Value, ".workspace", "state", "interior-shuffle.json")));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }
}
