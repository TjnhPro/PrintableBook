using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Core.Application.Desktop;

public sealed record ApplicationSnapshot(ApplicationDiscovery Discovery, DateTimeOffset RefreshedAt);

public interface IApplicationSnapshotService
{
    ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationSnapshotService(IApplicationRootDiscovery discovery) : IApplicationSnapshotService
{
    public async ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
        new(await discovery.DiscoverAsync(cancellationToken), DateTimeOffset.UtcNow);
}
