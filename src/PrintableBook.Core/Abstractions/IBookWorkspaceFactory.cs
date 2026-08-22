using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Abstractions;

public interface IBookWorkspaceFactory
{
    ValueTask<BookWorkspace> CreateAsync(
        BookId bookId,
        DirectoryReference bookDirectory,
        CancellationToken cancellationToken = default);
}
