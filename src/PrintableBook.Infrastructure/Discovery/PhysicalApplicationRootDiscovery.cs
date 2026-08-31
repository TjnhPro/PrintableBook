using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Infrastructure.Discovery;

public sealed class PhysicalApplicationRootDiscovery(IFileSystem fileSystem, IBookWorkspaceFactory workspaceFactory, Func<string>? baseDirectoryProvider = null) : IApplicationRootDiscovery
{
    public async ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var root = new DirectoryReference(baseDirectoryProvider?.Invoke() ?? AppDomain.CurrentDomain.BaseDirectory);
        var paths = new ApplicationPaths(root, new DirectoryReference(Path.Combine(root.Value, "brands")), new DirectoryReference(Path.Combine(root.Value, "sources")), new FileReference(Path.Combine(root.Value, "settings.json")));
        await fileSystem.CreateDirectoryAsync(paths.BrandsDirectory, cancellationToken);
        await fileSystem.CreateDirectoryAsync(paths.SourcesDirectory, cancellationToken);
        var brands = new List<DiscoveredBrand>();
        await foreach (var directory in fileSystem.EnumerateDirectoriesAsync(paths.BrandsDirectory, cancellationToken))
        {
            brands.Add(new DiscoveredBrand(
                Path.GetFileName(directory.Value),
                directory,
                await DiscoverBrandAssetsAsync(directory, cancellationToken),
                await DiscoverIntroTemplateAssetsAsync(directory, cancellationToken)));
        }
        var books = new List<DiscoveredBook>();
        await foreach (var directory in fileSystem.EnumerateDirectoriesAsync(paths.SourcesDirectory, cancellationToken))
        {
            var name = Path.GetFileName(directory.Value);
            var id = new BookId(name);
            books.Add(new DiscoveredBook(name, id, directory, await workspaceFactory.CreateAsync(id, directory, cancellationToken)));
        }
        return new ApplicationDiscovery(paths, brands.OrderBy(brand => brand.Name, StringComparer.OrdinalIgnoreCase).ToArray(), books.OrderBy(book => book.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async ValueTask<IReadOnlyList<DiscoveredBrandAsset>> DiscoverBrandAssetsAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            ("IntroTemplate", "Folder", true),
            ("AppPlus", "Folder", true),
            ("BackCover.psd", "File", false),
            ("frame.png", "Image", false),
            ("background.png", "Image", false),
            ("brand.json", "Settings", false)
        };
        var assets = new List<DiscoveredBrandAsset>(candidates.Length);
        foreach (var (name, type, isDirectory) in candidates)
        {
            var path = Path.Combine(brandDirectory.Value, name);
            var exists = isDirectory
                ? await fileSystem.DirectoryExistsAsync(new DirectoryReference(path), cancellationToken)
                : await fileSystem.FileExistsAsync(new FileReference(path), cancellationToken);
            assets.Add(new DiscoveredBrandAsset(name, type, exists ? "Present" : "Missing", path));
        }
        return assets;
    }

    private async ValueTask<IReadOnlyList<DiscoveredIntroTemplateAsset>> DiscoverIntroTemplateAssetsAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken)
    {
        var templateDirectory = new DirectoryReference(Path.Combine(brandDirectory.Value, "IntroTemplate"));
        if (!await fileSystem.DirectoryExistsAsync(templateDirectory, cancellationToken)) return [];

        var files = new List<FileReference>();
        await CollectIntroImagesAsync(templateDirectory, files, cancellationToken);
        var assets = files.Select(source => new DiscoveredIntroTemplateAsset(
            IntroTemplateSourceKey.FromTemplateRoot(templateDirectory, source),
            source.Value,
            Path.GetFileName(source.Value),
            new Uri(Path.GetFullPath(source.Value)).AbsoluteUri)).ToList();

        return assets
            .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async ValueTask CollectIntroImagesAsync(DirectoryReference directory, List<FileReference> files, CancellationToken cancellationToken)
    {
        await foreach (var file in fileSystem.EnumerateFilesAsync(directory, cancellationToken))
        {
            if (IsSupportedIntroImage(file.Value)) files.Add(file);
        }
        await foreach (var child in fileSystem.EnumerateDirectoriesAsync(directory, cancellationToken))
        {
            await CollectIntroImagesAsync(child, files, cancellationToken);
        }
    }

    private static bool IsSupportedIntroImage(string path) =>
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), ".jpg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), ".jpeg", StringComparison.OrdinalIgnoreCase);
}
