using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IBookCoverSelectionService
{
    ValueTask SelectAsync(string bookId, string coverReference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists an explicit choice when a source folder contains several cover candidates.
/// </summary>
public sealed class BookCoverSelectionService(
    IApplicationRootDiscovery discovery,
    IBookSourceScanner sourceScanner,
    IBookWorkspaceStateStore stateStore) : IBookCoverSelectionService
{
    public async ValueTask SelectAsync(string bookId, string coverReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bookId)) throw new ArgumentException("A Book id is required.", nameof(bookId));
        if (string.IsNullOrWhiteSpace(coverReference)) throw new ArgumentException("A cover reference is required.", nameof(coverReference));

        var book = (await discovery.DiscoverAsync(cancellationToken)).Books
            .FirstOrDefault(candidate => string.Equals(candidate.Id.Value, bookId, StringComparison.Ordinal));
        if (book is null) throw new ArgumentException("The selected Book no longer exists.", nameof(bookId));

        var scan = await sourceScanner.ScanAsync(book.Id, book.Directory, cancellationToken);
        var candidates = scan.Source?.GetAssets(BookAssetKind.Cover) ?? [];
        if (!candidates.Any(candidate => string.Equals(candidate.Reference, coverReference, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The selected cover is not a discovered cover candidate.", nameof(coverReference));
        }

        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        await stateStore.SaveAsync(book.Workspace, state.SelectCover(coverReference), cancellationToken);
    }
}
