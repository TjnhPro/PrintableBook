using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IBookInteriorSettingsService
{
    ValueTask SetHasBackgroundAsync(DiscoveredBook book, bool enabled, CancellationToken cancellationToken = default);
    ValueTask SetActiveAsync(DiscoveredBook book, FileReference source, bool isActive, CancellationToken cancellationToken = default);
}

public sealed class BookInteriorSettingsService(IBookWorkspaceStateStore stateStore) : IBookInteriorSettingsService
{
    public async ValueTask SetHasBackgroundAsync(DiscoveredBook book, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        await stateStore.SaveAsync(book.Workspace, state.SetHasBackground(enabled), cancellationToken);
    }

    public async ValueTask SetActiveAsync(DiscoveredBook book, FileReference source, bool isActive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(source);
        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        await stateStore.SaveAsync(book.Workspace, state.SetInteriorActive(InteriorSourceKey.FromBookRoot(book.Directory, source), isActive), cancellationToken);
    }
}
