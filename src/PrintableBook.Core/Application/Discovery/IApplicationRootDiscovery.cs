using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.Discovery;

public sealed record ApplicationPaths(DirectoryReference Root, DirectoryReference BrandsDirectory, DirectoryReference SourcesDirectory, FileReference SettingsFile);
public sealed record DiscoveredBrandAsset(string Name, string Type, string Status, string Path);
public sealed record DiscoveredIntroTemplateAsset(string Key, string SourceReference, string FileName, string LocalImageUrl);
public sealed record DiscoveredBrand(string Name, DirectoryReference Directory, IReadOnlyList<DiscoveredBrandAsset>? Assets = null, IReadOnlyList<DiscoveredIntroTemplateAsset>? IntroTemplateAssets = null);
public sealed record DiscoveredBook(string Name, BookId Id, DirectoryReference Directory, BookWorkspace Workspace);
public sealed record ApplicationDiscovery(ApplicationPaths Paths, IReadOnlyList<DiscoveredBrand> Brands, IReadOnlyList<DiscoveredBook> Books);

public interface IApplicationRootDiscovery
{
    ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default);
}
