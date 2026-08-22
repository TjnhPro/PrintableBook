using ImageMagick;
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
    public async Task ScanAsync_classifies_real_pngs_in_all_supported_groups_and_orders_interior_deterministically()
    {
        await CreatePngAsync("Cover", "cover.PNG");
        await CreatePngAsync("Intro", "intro-02.png");
        await CreatePngAsync("Intro", "intro-01.PNG");
        await CreatePngAsync("Interior", "page-02.PNG");
        await CreatePngAsync("Interior", "page-01.png");
        await CreateFileAsync("Interior", "notes.txt");
        await CreatePngAsync("Colored", "preview.PNG");

        var result = await CreateScanner().ScanAsync(new BookId("book-one"), new DirectoryReference(rootPath));

        Assert.True(result.IsSuccess);
        Assert.Equal(["page-01.png", "page-02.PNG"], result.Source!.GetAssets(BookAssetKind.Interior).Select(asset => Path.GetFileName(asset.Reference)));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Cover));
        Assert.Equal(["intro-01.PNG", "intro-02.png"], result.Source.GetAssets(BookAssetKind.Intro).Select(asset => Path.GetFileName(asset.Reference)));
        Assert.Single(result.Source.GetAssets(BookAssetKind.Colored));
        Assert.Equal(6, result.Source.Assets.Count);
    }

    [Fact]
    public async Task ScanAsync_accepts_empty_optional_groups_and_keeps_a_corrupt_png_as_an_interior_asset_for_downstream_validation()
    {
        await CreatePngAsync("Cover", "cover.png");
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
        await CreatePngAsync("Cover", "cover.png");
        for (var index = 1; index <= 90; index++)
        {
            await CreatePngAsync("Interior", $"page-{index:D4}.png");
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

    private Task CreatePngAsync(string group, string name)
    {
        var directory = Path.Combine(rootPath, group);
        Directory.CreateDirectory(directory);
        using var image = new MagickImage(MagickColors.White, 12, 12);
        image.GetPixels().SetPixel(6, 6, [0, 0, 0]);
        image.Write(Path.Combine(directory, name));
        return Task.CompletedTask;
    }
}
