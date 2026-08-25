using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IInteriorFrameModeService
{
    ValueTask SetAsync(DiscoveredBook book, FileReference source, FrameMode mode, CancellationToken cancellationToken = default);
}

public sealed class InteriorFrameModeService(
    IBookWorkspaceStateStore stateStore) : IInteriorFrameModeService
{
    public async ValueTask SetAsync(DiscoveredBook book, FileReference source, FrameMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported frame mode.");
        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        await stateStore.SaveAsync(book.Workspace, state.SetInteriorFrameMode(InteriorSourceKey.FromBookRoot(book.Directory, source), mode), cancellationToken);
    }
}
