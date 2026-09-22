using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class JsonProductionWorkspaceStateStoreTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ProductionState.{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_round_trips_asset_signatures_case_insensitively()
    {
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(
            new BookId("book"),
            new DirectoryReference(Path.Combine(rootPath, "Book")));
        var store = new JsonProductionWorkspaceStateStore(fileSystem);
        var signature = new ProductionFileSignature(123, DateTimeOffset.Parse("2026-09-21T11:30:00Z"));
        var state = ProductionWorkspaceState.Empty.RecordImportedAsset(
            ProductionAssetKind.BookOwner,
            signature,
            DateTimeOffset.Parse("2026-09-21T11:31:00Z"));

        await store.SaveAsync(workspace, state);
        var restored = await store.LoadAsync(workspace);

        Assert.Equal(signature, restored.GetAsset(ProductionAssetKind.BookOwner)!.Signature);
        Assert.True(restored.Assets!.ContainsKey("INTERIOR_BOOK_OWNER.PNG"));
    }

    [Fact]
    public async Task LoadAsync_returns_empty_for_an_unknown_schema_without_rewriting_the_file()
    {
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(
            new BookId("book"),
            new DirectoryReference(Path.Combine(rootPath, "Book")));
        var file = ProductionWorkspacePaths.StateFile(workspace);
        await File.WriteAllTextAsync(file.Value, "{\"schemaVersion\":999}");
        var store = new JsonProductionWorkspaceStateStore(fileSystem);

        var restored = await store.LoadAsync(workspace);

        Assert.Equal(ProductionWorkspaceState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Null(restored.Assets);
        Assert.Contains("999", await File.ReadAllTextAsync(file.Value), StringComparison.Ordinal);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
