using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IBookInteriorSettingsService
{
    ValueTask SetHasBackgroundAsync(DiscoveredBook book, bool enabled, CancellationToken cancellationToken = default);
    ValueTask SetActiveAsync(DiscoveredBook book, FileReference source, bool isActive, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(DiscoveredBook book, BookInteriorSettingsChange change, CancellationToken cancellationToken = default);
}

public sealed record InteriorAssetSettingsChange(FileReference Source, bool? IsActive, FrameMode? FrameMode);

public sealed record BookInteriorSettingsChange(
    bool? HasBackground,
    IReadOnlyList<InteriorAssetSettingsChange> Assets,
    bool? HasIntro = null,
    IReadOnlyList<FileReference>? IntroInteriorSources = null);

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

    public async ValueTask SaveAsync(DiscoveredBook book, BookInteriorSettingsChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(change);

        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        if (change.HasBackground is { } hasBackground) state = state.SetHasBackground(hasBackground);
        if (change.HasIntro is { } hasIntro) state = state.SetHasIntro(hasIntro);
        if (change.IntroInteriorSources is not null)
        {
            state = state.SetIntroInteriorSourceKeys(change.IntroInteriorSources.Select(source => InteriorSourceKey.FromBookRoot(book.Directory, source)));
        }

        foreach (var asset in change.Assets)
        {
            ArgumentNullException.ThrowIfNull(asset);
            ArgumentNullException.ThrowIfNull(asset.Source);
            var key = InteriorSourceKey.FromBookRoot(book.Directory, asset.Source);
            if (asset.IsActive is { } isActive) state = state.SetInteriorActive(key, isActive);
            if (asset.FrameMode is { } frameMode)
            {
                if (!Enum.IsDefined(frameMode)) throw new ArgumentOutOfRangeException(nameof(change), frameMode, "Unsupported frame mode.");
                state = state.SetInteriorFrameMode(key, frameMode);
            }
        }

        await stateStore.SaveAsync(book.Workspace, state, cancellationToken);
    }
}
