using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IInterruptedProcessingRecoveryService
{
    ValueTask RecoverAsync(CancellationToken cancellationToken = default);
}

public sealed class InterruptedProcessingRecoveryService(
    IApplicationRootDiscovery discovery,
    IBookWorkspaceStateStore stateStore) : IInterruptedProcessingRecoveryService
{
    public async ValueTask RecoverAsync(CancellationToken cancellationToken = default)
    {
        var application = await discovery.DiscoverAsync(cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var book in application.Books)
        {
            var state = await stateStore.LoadAsync(book.Workspace, cancellationToken);
            if (state?.Status != BookProcessingStatus.Running) continue;
            await stateStore.SaveAsync(book.Workspace, state.Interrupt(timestamp), cancellationToken);
        }
    }
}
