using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Infrastructure.Brands;

public sealed class PhysicalBrandCloneService : IBrandCloneService
{
    private const string ValidationFileName = "brand.validation.json";
    private readonly Func<string, string, CancellationToken, ValueTask> copyFile;

    public PhysicalBrandCloneService()
        : this(CopyFileAsync)
    {
    }

    internal PhysicalBrandCloneService(Func<string, string, CancellationToken, ValueTask> copyFile)
    {
        this.copyFile = copyFile;
    }

    public async ValueTask<BrandCloneResult> CloneAsync(
        ApplicationPaths paths,
        DiscoveredBrand sourceBrand,
        SupportedLanguageOption language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(sourceBrand);
        ArgumentNullException.ThrowIfNull(language);

        if (!SupportedLanguageCatalog.TryGet(language.Code, out var canonicalLanguage))
        {
            throw new BrandCloneException("brand_clone_language_invalid", "The Brand clone language is not supported.");
        }

        var sourcePath = Path.GetFullPath(sourceBrand.Directory.Value);
        if (!Directory.Exists(sourcePath))
        {
            throw new BrandCloneException("brand_clone_source_not_found", "The source Brand no longer exists.");
        }

        var destinationName = LanguageEditionNamingPolicy.CreateDestinationName(sourceBrand.Name, canonicalLanguage);
        var brandsPath = Path.GetFullPath(paths.BrandsDirectory.Value);
        var destinationPath = Path.Combine(brandsPath, destinationName);
        if (DestinationExists(brandsPath, destinationName))
        {
            throw new BrandCloneException("brand_clone_destination_exists", "The destination Brand already exists.");
        }

        var applicationRoot = Path.GetFullPath(paths.Root.Value);
        var stagingPath = Path.Combine(applicationRoot, $".brand-clone-{Guid.NewGuid():N}");
        var published = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stagingPath);
            await CopyDirectoryAsync(sourcePath, stagingPath, isRoot: true, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (DestinationExists(brandsPath, destinationName))
            {
                throw new BrandCloneException("brand_clone_destination_exists", "The destination Brand already exists.");
            }

            try
            {
                Directory.Move(stagingPath, destinationPath);
                published = true;
            }
            catch (IOException exception) when (DestinationExists(brandsPath, destinationName))
            {
                throw new BrandCloneException("brand_clone_destination_exists", "The destination Brand already exists.", exception);
            }

            return new BrandCloneResult(
                sourceBrand,
                canonicalLanguage,
                new DiscoveredBrand(destinationName, new DirectoryReference(destinationPath)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BrandCloneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new BrandCloneException("brand_clone_failed", "The Brand could not be cloned.", exception);
        }
        finally
        {
            if (!published && Directory.Exists(stagingPath))
            {
                try { Directory.Delete(stagingPath, recursive: true); }
                catch { /* Staging is intentionally outside brands, so discovery cannot treat it as a Brand. */ }
            }
        }
    }

    private async ValueTask CopyDirectoryAsync(string sourcePath, string destinationPath, bool isRoot, CancellationToken cancellationToken)
    {
        RejectReparsePoint(sourcePath);
        foreach (var directory in Directory.EnumerateDirectories(sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(directory);
            var childDestination = Path.Combine(destinationPath, Path.GetFileName(directory));
            Directory.CreateDirectory(childDestination);
            await CopyDirectoryAsync(directory, childDestination, isRoot: false, cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(file);
            if (isRoot && string.Equals(Path.GetFileName(file), ValidationFileName, StringComparison.OrdinalIgnoreCase)) continue;
            await copyFile(file, Path.Combine(destinationPath, Path.GetFileName(file)), cancellationToken);
        }
    }

    private static bool DestinationExists(string brandsPath, string destinationName)
    {
        if (!Directory.Exists(brandsPath)) return false;
        return Directory.EnumerateDirectories(brandsPath)
            .Select(Path.GetFileName)
            .Any(name => string.Equals(name, destinationName, StringComparison.OrdinalIgnoreCase));
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new BrandCloneException("brand_clone_failed", "The Brand contains an unsupported linked entry.");
        }
    }

    private static async ValueTask CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
    }
}
