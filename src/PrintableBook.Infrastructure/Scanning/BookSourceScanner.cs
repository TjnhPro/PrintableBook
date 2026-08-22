using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Infrastructure.Scanning;

public sealed class BookSourceScanner(IFileSystem fileSystem) : IBookSourceScanner
{
    private static readonly (string Directory, BookAssetKind Kind)[] SourceGroups =
    [
        ("Cover", BookAssetKind.Cover),
        ("Intro", BookAssetKind.Intro),
        ("Interior", BookAssetKind.Interior),
        ("Colored", BookAssetKind.Colored)
    ];

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

        foreach (var (directoryName, kind) in SourceGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceDirectory = new DirectoryReference(Path.Combine(bookDirectory.Value, directoryName));
            if (!await fileSystem.DirectoryExistsAsync(sourceDirectory, cancellationToken))
            {
                continue;
            }

            await foreach (var file in fileSystem.EnumerateFilesAsync(sourceDirectory, cancellationToken))
            {
                if (string.Equals(Path.GetExtension(file.Value), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    assets.Add(new BookAsset(file.Value, kind));
                }
            }
        }

        var source = new BookSource(assets.OrderBy(asset => asset.Reference, StringComparer.OrdinalIgnoreCase));
        if (source.GetAssets(BookAssetKind.Cover).Count == 0)
        {
            return BookSourceScanResult.Failed(new ProcessingFailure("book.cover_missing", "The book does not contain a Cover PNG."));
        }

        if (source.GetAssets(BookAssetKind.Interior).Count == 0)
        {
            return BookSourceScanResult.Failed(new ProcessingFailure("book.interior_empty", "The book does not contain any Interior PNG files."));
        }

        return BookSourceScanResult.Succeeded(source);
    }
}
