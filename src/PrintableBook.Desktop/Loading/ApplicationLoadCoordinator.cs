using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Desktop.Loading;

public enum ApplicationLoadKind
{
    Initial,
    Refresh
}

public sealed class ApplicationLoadCoordinator(
    IApplicationSnapshotService snapshotService,
    IInterruptedProcessingRecoveryService interruptedRecoveryService)
{
    private readonly Lock sync = new();
    private Task<ApplicationSnapshot>? activeRefresh;
    private bool initialRecoveryCompleted;

    public ValueTask<ApplicationSnapshot> RefreshAsync(
        ApplicationLoadKind kind,
        CancellationToken cancellationToken = default)
    {
        Task<ApplicationSnapshot> shared;

        lock (sync)
        {
            shared = activeRefresh is { IsCompleted: false }
                ? activeRefresh
                : activeRefresh = RunRefreshAsync();
        }

        return new ValueTask<ApplicationSnapshot>(shared.WaitAsync(cancellationToken));
    }

    private async Task<ApplicationSnapshot> RunRefreshAsync()
    {
        try
        {
            return await Task.Run(ExecuteRefreshAsync, CancellationToken.None);
        }
        finally
        {
            lock (sync)
            {
                activeRefresh = null;
            }
        }
    }

    private async Task<ApplicationSnapshot> ExecuteRefreshAsync()
    {
        bool recover;
        lock (sync)
        {
            recover = !initialRecoveryCompleted;
        }

        if (recover)
        {
            await interruptedRecoveryService.RecoverAsync();
            lock (sync)
            {
                initialRecoveryCompleted = true;
            }
        }

        return await snapshotService.RefreshAsync();
    }
}
