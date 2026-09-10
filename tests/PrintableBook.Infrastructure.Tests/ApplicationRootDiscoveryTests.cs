using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Discovery;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class ApplicationRootDiscoveryTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.AppDiscovery.{Guid.NewGuid():N}");

    [Fact]
    public async Task DiscoverAsync_creates_application_paths_and_discovers_direct_brand_and_book_folders_with_workspaces()
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "brands", "Amazon"));
        Directory.CreateDirectory(Path.Combine(rootPath, "brands", "Studio"));
        Directory.CreateDirectory(Path.Combine(rootPath, "sources", "Book One"));
        Directory.CreateDirectory(Path.Combine(rootPath, "sources", "Book Two"));
        var fileSystem = new PhysicalFileSystem();
        var discovery = new PhysicalApplicationRootDiscovery(fileSystem, new PhysicalBookWorkspaceFactory(fileSystem), () => rootPath);

        var snapshot = await discovery.DiscoverAsync();

        Assert.Equal(Path.Combine(rootPath, "settings.json"), snapshot.Paths.SettingsFile.Value);
        Assert.Equal(["Amazon", "Studio"], snapshot.Brands.Select(brand => brand.Name));
        Assert.Equal(["Book One", "Book Two"], snapshot.Books.Select(book => book.Name));
        Assert.All(snapshot.Books, book => Assert.True(Directory.Exists(Path.Combine(book.Directory.Value, ".workspace"))));
        Assert.All(snapshot.Brands, brand => Assert.Equal(4, brand.Assets!.Count));
    }

    [Fact]
    public async Task DiscoverAsync_exposes_sorted_portable_intro_template_assets()
    {
        var template = Path.Combine(rootPath, "brands", "Amazon", "IntroTemplate");
        Directory.CreateDirectory(template);
        await File.WriteAllTextAsync(Path.Combine(template, "z-last.jpg"), "test");
        await File.WriteAllTextAsync(Path.Combine(template, "a-first.png"), "test");
        var fileSystem = new PhysicalFileSystem();
        var discovery = new PhysicalApplicationRootDiscovery(fileSystem, new PhysicalBookWorkspaceFactory(fileSystem), () => rootPath);

        var brand = Assert.Single((await discovery.DiscoverAsync()).Brands);

        Assert.Equal(["a-first.png", "z-last.jpg"], brand.IntroTemplateAssets!.Select(asset => asset.Key));
        Assert.All(brand.IntroTemplateAssets!, asset => Assert.StartsWith("file:", asset.LocalImageUrl, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_uses_the_validation_intro_extension_policy_for_nested_assets_and_collision_safe_keys()
    {
        var template = Path.Combine(rootPath, "brands", "Amazon", "IntroTemplate");
        Directory.CreateDirectory(Path.Combine(template, "nested"));
        await File.WriteAllTextAsync(Path.Combine(template, "cover.jpeg"), "test");
        await File.WriteAllTextAsync(Path.Combine(template, "nested", "cover.png"), "test");
        await File.WriteAllTextAsync(Path.Combine(template, "nested", "ignored.gif"), "test");
        var fileSystem = new PhysicalFileSystem();
        var discovery = new PhysicalApplicationRootDiscovery(fileSystem, new PhysicalBookWorkspaceFactory(fileSystem), () => rootPath);

        var assets = Assert.Single((await discovery.DiscoverAsync()).Brands).IntroTemplateAssets!;

        Assert.Equal(["cover.jpeg", "nested/cover.png"], assets.Select(asset => asset.Key));
        Assert.Equal(assets.Count, assets.Select(asset => asset.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task DiscoverAsync_exposes_folder_entries_and_image_dimensions_for_the_brand_asset_inventory()
    {
        var intro = Path.Combine(rootPath, "brands", "Amazon", "IntroTemplate");
        var appPlus = Path.Combine(rootPath, "brands", "Amazon", "AppPlus", "nested");
        Directory.CreateDirectory(intro);
        Directory.CreateDirectory(appPlus);
        await File.WriteAllTextAsync(Path.Combine(intro, "intro.png"), "test");
        await File.WriteAllTextAsync(Path.Combine(appPlus, "badge.jpg"), "test");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "brands", "Amazon", "frame.png"), "test");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "brands", "Amazon", "background.png"), "test");
        var fileSystem = new PhysicalFileSystem();
        var discovery = new PhysicalApplicationRootDiscovery(fileSystem, new PhysicalBookWorkspaceFactory(fileSystem), () => rootPath, new FixedSizeImageInspector());

        var assets = Assert.Single((await discovery.DiscoverAsync()).Brands).Assets!;

        var introAsset = Assert.Single(assets, asset => asset.Name == "IntroTemplate");
        var introEntry = Assert.Single(introAsset.Entries!);
        Assert.Equal("intro.png", introEntry.Name);
        Assert.Equal(".png", introEntry.Extension);
        Assert.Equal(new ImageSize(1024, 1024), introEntry.Size);
        Assert.Equal("Present", introEntry.Status);
        var appPlusAsset = Assert.Single(assets, asset => asset.Name == "AppPlus");
        Assert.Equal("nested/badge.jpg", Assert.Single(appPlusAsset.Entries!).Name);
        Assert.Equal(new ImageSize(1024, 1024), Assert.Single(appPlusAsset.Entries!).Size);
        Assert.Equal(new ImageSize(1024, 1024), Assert.Single(assets, asset => asset.Name == "frame.png").Size);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private sealed class FixedSizeImageInspector : IImageInspector
    {
        public ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ImageSize(1024, 1024));
        public ValueTask<ImageInfo> GetInfoAsync(FileReference image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
