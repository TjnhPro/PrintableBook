using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IInteriorFrameModeService
{
    ValueTask SetAsync(string bookId, string sourceReference, FrameMode mode, CancellationToken cancellationToken = default);
}

public sealed class InteriorFrameModeService(
    IApplicationRootDiscovery discovery,
    IBookSourceScanner sourceScanner,
    IBookWorkspaceStateStore stateStore) : IInteriorFrameModeService
{
    public async ValueTask SetAsync(string bookId, string sourceReference, FrameMode mode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bookId)) throw new ArgumentException("A Book id is required.", nameof(bookId));
        if (string.IsNullOrWhiteSpace(sourceReference)) throw new ArgumentException("An interior source reference is required.", nameof(sourceReference));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported frame mode.");
        var book = (await discovery.DiscoverAsync(cancellationToken)).Books.FirstOrDefault(candidate => string.Equals(candidate.Id.Value, bookId, StringComparison.Ordinal));
        if (book is null) throw new ArgumentException("The selected Book no longer exists.", nameof(bookId));
        var scan = await sourceScanner.ScanAsync(book.Id, book.Directory, cancellationToken);
        var source = scan.Source?.GetAssets(BookAssetKind.Interior).FirstOrDefault(asset => string.Equals(asset.Reference, sourceReference, StringComparison.OrdinalIgnoreCase));
        if (source is null) throw new ArgumentException("The source is not a discovered interior asset.", nameof(sourceReference));
        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        await stateStore.SaveAsync(book.Workspace, state.SetInteriorFrameMode(InteriorSourceKey.FromBookRoot(book.Directory, new FileReference(source.Reference)), mode), cancellationToken);
    }
}
