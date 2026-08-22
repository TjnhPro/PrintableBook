using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;

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
        Assert.True(Directory.Exists(Path.Combine(bookDirectory.Value, ".workspace", "output")));
        var restored = await store.LoadAsync(workspace);
        Assert.Equal(BookProcessingStatus.Failed, restored!.Status);
        Assert.Equal("resize", restored.FailedStep);
        Assert.True(File.Exists(Path.Combine(bookDirectory.Value, ".workspace", "errors", "latest-error.json")));
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
