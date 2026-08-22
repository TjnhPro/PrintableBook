using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.Scanning;

public interface IBookSourceScanner
{
    ValueTask<BookSourceScanResult> ScanAsync(
        BookId bookId,
        DirectoryReference bookDirectory,
        CancellationToken cancellationToken = default);
}
