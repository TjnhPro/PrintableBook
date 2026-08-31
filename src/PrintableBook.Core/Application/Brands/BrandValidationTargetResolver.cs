using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Brands;

public sealed record ResolvedBrandValidationEntry(BrandValidationEntry Entry, IReadOnlyList<FileReference> Files);

public sealed class BrandValidationTargetResolver(IFileSystem fileSystem)
{
    public async ValueTask<IReadOnlyList<ResolvedBrandValidationEntry>> ResolveAsync(
        DirectoryReference brandDirectory,
        BrandValidationDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ResolvedBrandValidationEntry>(definition.Entries.Count);
        foreach (var entry in definition.Entries)
        {
            var files = entry.Target switch
            {
                BrandValidationFileTarget file => [new FileReference(Path.Combine(brandDirectory.Value, file.RelativePath))],
                BrandValidationDirectoryFilesTarget directory => await ResolveDirectoryFilesAsync(brandDirectory, directory, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported Brand validation target '{entry.Target.GetType().Name}'.")
            };
            results.Add(new ResolvedBrandValidationEntry(entry, files));
        }

        return results;
    }

    private async ValueTask<IReadOnlyList<FileReference>> ResolveDirectoryFilesAsync(
        DirectoryReference brandDirectory,
        BrandValidationDirectoryFilesTarget target,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryReference(Path.Combine(brandDirectory.Value, target.RelativePath));
        if (!await fileSystem.DirectoryExistsAsync(directory, cancellationToken)) return [];

        var files = new List<FileReference>();
        await CollectAsync(directory, target, files, cancellationToken);
        return files.OrderBy(file => NormalizeRelativePath(brandDirectory, file), StringComparer.Ordinal).ToArray();
    }

    private async ValueTask CollectAsync(DirectoryReference directory, BrandValidationDirectoryFilesTarget target, List<FileReference> files, CancellationToken cancellationToken)
    {
        await foreach (var file in fileSystem.EnumerateFilesAsync(directory, cancellationToken))
        {
            if (target.Extensions.Contains(Path.GetExtension(file.Value))) files.Add(file);
        }
        if (!target.Recursive) return;
        await foreach (var child in fileSystem.EnumerateDirectoriesAsync(directory, cancellationToken))
        {
            await CollectAsync(child, target, files, cancellationToken);
        }
    }

    public static string NormalizeRelativePath(DirectoryReference brandDirectory, FileReference file) =>
        Path.GetRelativePath(brandDirectory.Value, file.Value)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .ToLowerInvariant();
}
