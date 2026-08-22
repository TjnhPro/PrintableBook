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
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
