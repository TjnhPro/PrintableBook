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
    public async Task ScanAsync_discovers_all_files_without_validating_extensions()
    {
        await CreateFileAsync("Cover", "cover.PNG");
        await CreateFileAsync("Intro", "intro-02.png");
        await CreateFileAsync("Intro", "intro-01.PNG");
        await CreateFileAsync("Interior", "page-02.PNG");
        await CreateFileAsync("Interior", "page-01.png");
        await CreateFileAsync("Interior", "notes.txt");
        await CreateFileAsync("Colored", "preview.PNG");

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        Assert.Equal(["notes.txt", "page-01.png", "page-02.PNG"], result.Source!.GetAssets(BookAssetKind.Interior).Select(asset => Path.GetFileName(asset.Reference)));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Cover));
        Assert.Equal(["intro-01.PNG", "intro-02.png"], result.Source.GetAssets(BookAssetKind.Intro).Select(asset => Path.GetFileName(asset.Reference)));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Colored));
        Assert.Equal(7, result.Source.Assets.Count);
    }

    [Fact]
    public async Task ScanAsync_accepts_empty_optional_groups_and_keeps_a_corrupt_png_as_an_interior_asset_for_downstream_validation()
    {
        await CreateFileAsync("Cover", "cover.png");
        await CreateFileAsync("Interior", "corrupt.PNG");
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(Path.Combine(rootPath, "Intro")));
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(Path.Combine(rootPath, "Colored")));

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Source!.GetAssets(BookAssetKind.Intro));
        Assert.Empty(result.Source.GetAssets(BookAssetKind.Colored));
        Assert.Equal("corrupt.PNG", Path.GetFileName(Assert.Single(result.Source.GetAssets(BookAssetKind.Interior)).Reference));
    }

    [Fact]
    public async Task ScanAsync_handles_a_large_interior_corpus_without_duplicates()
    {
        await CreateFileAsync("Cover", "cover.png");
        for (var index = 1; index <= 90; index++)
        {
            await CreateFileAsync("Interior", $"page-{index:D4}.png");
        }

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        var interiors = result.Source!.GetAssets(BookAssetKind.Interior);
        Assert.Equal(90, interiors.Count);
        Assert.Equal(90, interiors.Select(asset => asset.Reference).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("page-0001.png", Path.GetFileName(interiors[0].Reference));
        Assert.Equal("page-0090.png", Path.GetFileName(interiors[^1].Reference));
    }

    [Fact]
    public async Task ScanAsync_accepts_an_interior_only_book_when_cover_is_missing()
    {
        await CreateFileAsync("Interior", "page-01.png");

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Source!.GetAssets(BookAssetKind.Cover));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Interior));
    }

    [Fact]
    public async Task ScanAsync_discovers_book_cover_assets_for_ui_previews()
    {
        await CreateFileAsync("Book cover", "cover.png");
        await CreateFileAsync("Book interior", "page-01.png");

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        var cover = Assert.Single(result.Source!.GetAssets(BookAssetKind.Cover));
        Assert.Equal("cover.png", Path.GetFileName(cover.Reference));
    }

    [Fact]
    public async Task ScanAsync_succeeds_when_interior_folder_is_empty()
    {
        await CreateFileAsync("Cover", "cover.png");
        await fileSystem.CreateDirectoryAsync(new DirectoryReference(Path.Combine(rootPath, "Interior")));

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Source!.GetAssets(BookAssetKind.Interior));
    }

    [Fact]
    public async Task ScanAsync_supports_the_discovered_book_folder_layout_and_jpeg_interiors()
    {
        await CreateFileAsync("Source cover", "page-001.png");
        await CreateFileAsync("Source cover", "page-002.png");
        await CreateFileAsync("Book interior", "page-001.jpg");
        await CreateFileAsync("Book interior", "page-002.jpeg");
        await CreateFileAsync("Book colored", "page-001.jpg");

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Source!.GetAssets(BookAssetKind.Cover).Count);
        Assert.Equal(["page-001.jpg", "page-002.jpeg"], result.Source.GetAssets(BookAssetKind.Interior).Select(asset => Path.GetFileName(asset.Reference)));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Colored));
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
