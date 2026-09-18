using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Infrastructure.Scanning;

public sealed class BookSourceScanner(IFileSystem fileSystem) : IBookSourceScanner
{
    private const string CloneBookDirectoryName = "Clone book";
    private const string MainBookDirectoryName = "Main book";
    private const string BookCoverDirectoryName = "Book cover";

    public async ValueTask<BookSourceScanResult> ScanAsync(
        BookId bookId,
        DirectoryReference bookDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookId);
        ArgumentNullException.ThrowIfNull(bookDirectory);

        if (!await fileSystem.DirectoryExistsAsync(bookDirectory, cancellationToken))
        {
            return BookSourceScanResult.Failed(new ProcessingFailure("book.directory_missing", "The selected book directory does not exist."));
        }

        var cloneBookDirectory = await ResolveChildDirectoryAsync(
            bookDirectory,
            CloneBookDirectoryName,
            cancellationToken);
        var processingRoot = cloneBookDirectory ?? bookDirectory;
        var layoutKind = cloneBookDirectory is null
            ? BookSourceLayoutKind.LegacyFlat
            : BookSourceLayoutKind.MainCloneNestedV1;
        var representativeImage = cloneBookDirectory is null
            ? null
            : await ResolveMainBookRepresentativeImageAsync(bookDirectory, cancellationToken);
        var assets = new List<BookAsset>();

        foreach (var (directoryName, kind) in BookSourceLayout.ProcessingFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceDirectory = await ResolveChildDirectoryAsync(
                processingRoot,
                directoryName,
                cancellationToken);
            if (sourceDirectory is null)
            {
                continue;
            }

            await foreach (var file in fileSystem.EnumerateFilesAsync(sourceDirectory, cancellationToken))
            {
                assets.Add(new BookAsset(file.Value, kind));
            }
        }

        var source = new BookSource(assets.OrderBy(asset => asset.Reference, StringComparer.OrdinalIgnoreCase));
        return BookSourceScanResult.Succeeded(
            source,
            new BookSourceScanMetadata(layoutKind, processingRoot, representativeImage));
    }

    private async ValueTask<FileReference?> ResolveMainBookRepresentativeImageAsync(
        DirectoryReference bookDirectory,
        CancellationToken cancellationToken)
    {
        var mainBookDirectory = await ResolveChildDirectoryAsync(
            bookDirectory,
            MainBookDirectoryName,
            cancellationToken);
        if (mainBookDirectory is null)
        {
            return null;
        }

        // The Main package has one deliberately narrow contract. Address its
        // Book cover directly so sibling Main folders are never enumerated.
        var coverDirectory = new DirectoryReference(Path.Combine(mainBookDirectory.Value, BookCoverDirectoryName));
        if (!await fileSystem.DirectoryExistsAsync(coverDirectory, cancellationToken))
        {
            return null;
        }

        var candidates = new List<FileReference>();
        await foreach (var file in fileSystem.EnumerateFilesAsync(coverDirectory, cancellationToken))
        {
            if (IsDirectChild(coverDirectory, file.Value) && BookSourceLayout.IsSupportedImage(file.Value))
            {
                candidates.Add(file);
            }
        }

        return candidates
            .OrderBy(file => file.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private async ValueTask<DirectoryReference?> ResolveChildDirectoryAsync(
        DirectoryReference parent,
        string childName,
        CancellationToken cancellationToken)
    {
        await foreach (var child in fileSystem.EnumerateDirectoriesAsync(parent, cancellationToken))
        {
            if (IsDirectChild(parent, child.Value) &&
                string.Equals(Path.GetFileName(child.Value), childName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsDirectChild(DirectoryReference parent, string candidate)
    {
        var relativePath = Path.GetRelativePath(parent.Value, candidate);
        return !Path.IsPathRooted(relativePath) &&
            !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
            !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(Path.GetDirectoryName(relativePath));
    }
}
