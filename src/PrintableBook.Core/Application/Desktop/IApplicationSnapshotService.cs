using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Core.Application.Desktop;

public sealed record ApplicationSnapshot(ApplicationDiscovery Discovery, GlobalSettings GlobalSettings, DateTimeOffset RefreshedAt);

public interface IApplicationSnapshotService
{
    ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationSnapshotService(IApplicationRootDiscovery discovery, IGlobalSettingsStore settingsStore) : IApplicationSnapshotService
{
    public async ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var discoverySnapshot = await discovery.DiscoverAsync(cancellationToken);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        return new ApplicationSnapshot(discoverySnapshot, settings, DateTimeOffset.UtcNow);
    }
}
