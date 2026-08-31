using PrintableBook.Core.Abstractions;
using PrintableBook.Infrastructure.FileSystem;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PhysicalFileSystemTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.Tests.{Guid.NewGuid():N}");
    private readonly PhysicalFileSystem fileSystem = new();

    [Fact]
    public async Task WriteTextAtomicallyAsync_creates_a_readable_file_and_enumerates_it()
    {
        var root = new DirectoryReference(rootPath);
        var file = new FileReference(Path.Combine(rootPath, "state", "book-state.json"));
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(Path.Combine(rootPath, "state")));

        await fileSystem.WriteTextAtomicallyAsync(file, "{\"status\":\"Running\"}");

        Assert.True(await fileSystem.FileExistsAsync(file));
        Assert.Equal("{\"status\":\"Running\"}", await fileSystem.ReadTextAsync(file));
        var files = await ToListAsync(fileSystem.EnumerateFilesAsync(new DirectoryReference(Path.Combine(rootPath, "state"))));
        Assert.Contains(files, found => found.Value == file.Value);
    }

    [Fact]
    public async Task MoveFileAsync_replaces_the_target_without_leaving_the_source()
    {
        var source = new FileReference(Path.Combine(rootPath, "source.txt"));
        var target = new FileReference(Path.Combine(rootPath, "target.txt"));
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(rootPath));
        await fileSystem.WriteTextAtomicallyAsync(source, "new");
        await fileSystem.WriteTextAtomicallyAsync(target, "old");

        await fileSystem.MoveFileAsync(source, target, overwrite: true);

        Assert.False(await fileSystem.FileExistsAsync(source));
        Assert.Equal("new", await fileSystem.ReadTextAsync(target));
    }

    [Fact]
    public async Task GetFileMetadataAsync_returns_length_and_utc_last_write_time()
    {
        var file = new FileReference(Path.Combine(rootPath, "metadata.txt"));
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(rootPath));
        await fileSystem.WriteTextAtomicallyAsync(file, "metadata");
        var expectedLastWrite = new DateTimeOffset(2026, 8, 31, 4, 32, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(file.Value, expectedLastWrite.UtcDateTime);

        var metadata = await fileSystem.GetFileMetadataAsync(file);

        Assert.Equal(8, metadata!.Value.LengthBytes);
        Assert.Equal(expectedLastWrite, metadata.Value.LastWriteTimeUtc);
    }

    [Fact]
    public async Task GetFileMetadataAsync_returns_null_when_file_is_missing()
    {
        var metadata = await fileSystem.GetFileMetadataAsync(new FileReference(Path.Combine(rootPath, "missing.txt")));

        Assert.Null(metadata);
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

    private static async Task<List<FileReference>> ToListAsync(IAsyncEnumerable<FileReference> files)
    {
        var result = new List<FileReference>();
        await foreach (var file in files)
        {
            result.Add(file);
        }

        return result;
    }
}
