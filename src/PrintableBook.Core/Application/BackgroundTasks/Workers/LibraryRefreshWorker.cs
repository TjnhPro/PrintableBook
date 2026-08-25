using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public sealed record LibraryRefreshRequest;

public sealed class LibraryRefreshWorker(
    IInterruptedProcessingRecoveryService recovery,
    IApplicationSnapshotService snapshotService) : BackgroundTaskWorker<LibraryRefreshRequest, ApplicationSnapshot>
{
    private int recoveryCompleted;

    public override BackgroundTaskKind Kind => BackgroundTaskKind.LibraryRefresh;

    protected override async ValueTask<ApplicationSnapshot> ExecuteTypedAsync(LibraryRefreshRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref recoveryCompleted) == 0)
        {
            context.Report("startup.recovery");
            await recovery.RecoverAsync(cancellationToken);
            Volatile.Write(ref recoveryCompleted, 1);
        }

        context.Report("snapshot.refresh");
        return await snapshotService.RefreshAsync(cancellationToken);
    }
}
