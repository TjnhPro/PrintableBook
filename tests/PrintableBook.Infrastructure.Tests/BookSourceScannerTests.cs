using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Scanning;

namespace PrintableBook.Infrastructure.Tests;

public sealed class BookSourceScannerTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ScannerTests.{Guid.NewGuid():N}");
    private readonly PhysicalFileSystem fileSystem = new();

    [Fact]
    public async Task ScanAsync_reads_only_png_assets_from_the_supported_source_groups()
    {
        await CreateFileAsync("Cover", "cover.png");
        await CreateFileAsync("Interior", "page-02.png");
        await CreateFileAsync("Interior", "page-01.png");
        await CreateFileAsync("Interior", "notes.txt");
        await CreateFileAsync("Colored", "preview.PNG");

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        Assert.Equal(["page-01.png", "page-02.png"], result.Source!.GetAssets(BookAssetKind.Interior).Select(asset => Path.GetFileName(asset.Reference)));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Cover));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Colored));
    }

    [Fact]
    public async Task ScanAsync_returns_a_structured_failure_when_cover_is_missing()
    {
        await CreateFileAsync("Interior", "page-01.png");

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.False(result.IsSuccess);
        Assert.Equal("book.cover_missing", result.Failure!.Code);
    }

    [Fact]
    public async Task ScanAsync_returns_a_structured_failure_when_interior_is_empty()
    {
        await CreateFileAsync("Cover", "cover.png");
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(Path.Combine(rootPath, "Interior")));

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.False(result.IsSuccess);
        Assert.Equal("book.interior_empty", result.Failure!.Code);
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

    private BookSourceScanner CreateScanner() => new(fileSystem);

    private async Task CreateFileAsync(string group, string name)
    {
        var directory = Path.Combine(rootPath, group);
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(directory));
        await fileSystem.WriteTextAtomicallyAsync(new FileReference(Path.Combine(directory, name)), "fixture");
    }
}
