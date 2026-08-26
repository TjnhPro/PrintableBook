using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PhysicalBookStorageMaintenanceTests : IAsyncLifetime
{
    [Fact]
    public async Task ClearHeavyProcessingCacheAsync_removes_processed_intro_rasters()
    {
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("intro"), new DirectoryReference(Path.Combine(rootPath, "Intro")));
        var intro = Path.Combine(workspace.ProcessedDirectory.Value, "intro", "intro-0001.png");
        await File.WriteAllBytesAsync(intro, [1, 2, 3]);

        var freed = await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(workspace);

        Assert.Equal(3, freed);
        Assert.False(File.Exists(intro));
    }

    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.StorageMaintenance.{Guid.NewGuid():N}");

    [Fact]
    public async Task ClearHeavyProcessingCacheAsync_keeps_metadata_state_sources_and_output()
    {
        var workspace = await CreateWorkspaceAsync("Book");
        var paths = await CreateCleanupTreeAsync(workspace);

        var freed = await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(workspace);

        Assert.Equal(83, freed);
        Assert.All(paths.Kept, path => Assert.True(File.Exists(path), path));
        Assert.All(paths.Deleted, path => Assert.False(File.Exists(path), path));
        Assert.True(Directory.Exists(Path.Combine(workspace.ProcessedDirectory.Value, "interior")));
        Assert.True(Directory.Exists(workspace.TemporaryOutputDirectory.Value));
    }

    [Fact]
    public async Task ClearHeavyProcessingCacheAsync_migrates_legacy_stamps_before_clearing_processed_interior()
    {
        var workspace = await CreateWorkspaceAsync("Legacy");
        var legacy = Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-0001.input-stamp.json");
        await File.WriteAllTextAsync(legacy, "legacy-stamp");
        await WriteBytesAsync(Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-0001.png"), 19);

        await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(workspace);

        var migrated = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001", "input-stamp.json");
        Assert.Equal("legacy-stamp", await File.ReadAllTextAsync(migrated));
        Assert.False(File.Exists(legacy));
        Assert.False(File.Exists(Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-0001.png")));
    }

    [Fact]
    public async Task ClearHeavyProcessingCacheAsync_keeps_existing_new_stamp_and_removes_legacy_duplicate()
    {
        var workspace = await CreateWorkspaceAsync("Duplicate");
        var cacheStamp = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001", "input-stamp.json");
        var legacy = Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-0001.input-stamp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheStamp)!);
        await File.WriteAllTextAsync(cacheStamp, "new-stamp");
        await File.WriteAllTextAsync(legacy, "legacy-stamp");

        await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(workspace);

        Assert.Equal("new-stamp", await File.ReadAllTextAsync(cacheStamp));
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public async Task ClearHeavyProcessingCacheAsync_is_idempotent()
    {
        var workspace = await CreateWorkspaceAsync("Idempotent");
        await CreateCleanupTreeAsync(workspace);
        var maintenance = new PhysicalBookStorageMaintenance();

        Assert.Equal(83, await maintenance.ClearHeavyProcessingCacheAsync(workspace));
        Assert.Equal(0, await maintenance.ClearHeavyProcessingCacheAsync(workspace));
    }

    [Fact]
    public async Task ClearHeavyProcessingCacheAsync_returns_actual_deleted_byte_count()
    {
        var workspace = await CreateWorkspaceAsync("FreedBytes");
        await CreateCleanupTreeAsync(workspace);

        var freed = await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(workspace);

        Assert.Equal(83, freed);
    }

    private async Task<BookWorkspace> CreateWorkspaceAsync(string name) =>
        await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId(name), new DirectoryReference(Path.Combine(rootPath, name)));

    private static async Task<(IReadOnlyList<string> Kept, IReadOnlyList<string> Deleted)> CreateCleanupTreeAsync(BookWorkspace workspace)
    {
        var book = Directory.GetParent(workspace.WorkingDirectory.Value)!.FullName;
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001");
        var classification = Path.Combine(cache, "classification.json");
        var stamp = Path.Combine(cache, "input-stamp.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var framed = Path.Combine(cache, "framed.png");
        var working = Path.Combine(cache, "working-page.png");
        var finalPage = Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-0001.png");
        var temporary = Path.Combine(workspace.TemporaryOutputDirectory.Value, "interior.pdf");
        var source = Path.Combine(book, "Book interior", "source.png");
        var output = Path.Combine(book, "Output", "Book - Interior.pdf");
        var state = Path.Combine(workspace.WorkingDirectory.Value, "state", "state.json");
        var log = Path.Combine(workspace.WorkingDirectory.Value, "logs", "processing.log");
        var error = Path.Combine(workspace.WorkingDirectory.Value, "errors", "failure.json");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(classification, "classification");
        await File.WriteAllTextAsync(stamp, "stamp");
        await WriteBytesAsync(prepared, 11);
        await WriteBytesAsync(framed, 13);
        await WriteBytesAsync(working, 17);
        await WriteBytesAsync(finalPage, 19);
        await WriteBytesAsync(temporary, 23);
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(output, "output");
        await File.WriteAllTextAsync(state, "state");
        await File.WriteAllTextAsync(log, "log");
        await File.WriteAllTextAsync(error, "error");
        return ([classification, stamp, source, output, state, log, error], [prepared, framed, working, finalPage, temporary]);
    }

    private static async Task WriteBytesAsync(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[length]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
