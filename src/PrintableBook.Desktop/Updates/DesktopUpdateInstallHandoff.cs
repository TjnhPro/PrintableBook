using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Desktop.Updates;

public sealed class DesktopUpdateInstallHandoff(
    ProcessWindowShutdownCoordinator shutdownCoordinator,
    IUpdaterProcessLauncher updaterProcessLauncher,
    IUpdateApplicationLifetime applicationLifetime,
    IUpdateRuntimeInfo runtimeInfo,
    IApplicationVersionProvider versionProvider,
    UpdateStorageLayout storageLayout) : IUpdateInstallHandoff
{
    public async ValueTask<UpdateInstallHandoffOutcome> BeginAsync(PreparedUpdate preparedUpdate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedUpdate);
        var closeOutcome = await shutdownCoordinator.RequestUpdateRestartAsync(cancellationToken);
        if (closeOutcome == ProcessWindowCloseOutcome.KeepOpen) return UpdateInstallHandoffOutcome.Cancelled;
        updaterProcessLauncher.Launch(new UpdaterLaunchRequest(runtimeInfo.ProcessId, runtimeInfo.AppRoot, preparedUpdate.PayloadDirectoryPath, storageLayout.RootPath, versionProvider.CurrentVersion));
        if (closeOutcome == ProcessWindowCloseOutcome.ForceExit) applicationLifetime.RequestForceExit();
        else applicationLifetime.RequestGracefulShutdown();
        return UpdateInstallHandoffOutcome.ShutdownScheduled;
    }
}
