using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class JsonBookWorkspaceStateStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.StateStore.{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_defaults_new_fields_when_legacy_json_omits_them()
    {
        var workspace = await CreateWorkspaceAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.WorkingDirectory.Value, "state", "book-state.json"), "{\"bookId\":{\"value\":\"book\"},\"status\":\"notStarted\",\"updatedAt\":\"0001-01-01T00:00:00+00:00\",\"mayResume\":false,\"inactiveInteriorSourceKeys\":[\"B.PNG\",\"b.png\",\" \",\"a.png\"]}");
        var state = await new JsonBookWorkspaceStateStore(new PhysicalFileSystem()).LoadAsync(workspace);
        Assert.False(state!.HasBackground);
        Assert.Equal(["a.png", "B.PNG"], state.InactiveInteriorSourceKeys);
    }

    [Fact]
    public async Task SaveAsync_round_trips_background_and_inactive_sources()
    {
        var workspace = await CreateWorkspaceAsync();
        var store = new JsonBookWorkspaceStateStore(new PhysicalFileSystem());
        var state = BookProcessingState.NotStarted(new BookId("book")).SetHasBackground(true).SetInteriorActive("Book interior/b.png", false);
        await store.SaveAsync(workspace, state);
        var restored = await store.LoadAsync(workspace);
        Assert.True(restored!.HasBackground);
        Assert.False(restored.IsInteriorActive("BOOK INTERIOR/B.PNG"));
    }

    private async Task<BookWorkspace> CreateWorkspaceAsync() => await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("book"), new DirectoryReference(Path.Combine(root, "book")));
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { if (Directory.Exists(root)) Directory.Delete(root, true); return Task.CompletedTask; }
}
