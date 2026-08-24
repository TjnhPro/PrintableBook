using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Infrastructure.Scanning;

public sealed class BookSourceScanner(IFileSystem fileSystem) : IBookSourceScanner
{
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

        var assets = new List<BookAsset>();

        foreach (var (directoryName, kind) in BookSourceLayout.ProcessingFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceDirectory = new DirectoryReference(Path.Combine(bookDirectory.Value, directoryName));
            if (!await fileSystem.DirectoryExistsAsync(sourceDirectory, cancellationToken))
            {
                continue;
            }

            await foreach (var file in fileSystem.EnumerateFilesAsync(sourceDirectory, cancellationToken))
            {
                if (BookSourceLayout.IsSupportedImage(file.Value))
                {
                    assets.Add(new BookAsset(file.Value, kind));
                }
            }
        }

        var source = new BookSource(assets.OrderBy(asset => asset.Reference, StringComparer.OrdinalIgnoreCase));
        if (source.GetAssets(BookAssetKind.Interior).Count == 0)
        {
            return BookSourceScanResult.Failed(new ProcessingFailure("book.interior_empty", "The book does not contain any readable interior images."), source);
        }

        return BookSourceScanResult.Succeeded(source);
    }
}
