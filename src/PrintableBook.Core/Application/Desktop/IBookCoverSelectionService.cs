using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IBookCoverSelectionService
{
    ValueTask SelectAsync(DiscoveredBook book, string coverReference, IReadOnlyList<BookAsset> discoveredCoverAssets, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists an explicit choice when a source folder contains several cover candidates.
/// </summary>
public sealed class BookCoverSelectionService(
    IBookWorkspaceStateStore stateStore) : IBookCoverSelectionService
{
    public async ValueTask SelectAsync(DiscoveredBook book, string coverReference, IReadOnlyList<BookAsset> discoveredCoverAssets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (string.IsNullOrWhiteSpace(coverReference)) throw new ArgumentException("A cover reference is required.", nameof(coverReference));
        ArgumentNullException.ThrowIfNull(discoveredCoverAssets);

        if (!discoveredCoverAssets.Any(candidate => candidate.Kind == BookAssetKind.Cover && string.Equals(candidate.Reference, coverReference, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The selected cover is not a discovered cover candidate.", nameof(coverReference));
        }

        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        await stateStore.SaveAsync(book.Workspace, state.SelectCover(coverReference), cancellationToken);
    }
}
