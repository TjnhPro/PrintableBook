using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Desktop.Updates;

public sealed class DesktopUpdateCoordinator(
    IUpdateService updateService,
    IUpdatePreparationService preparationService,
    IApplicationVersionProvider versionProvider,
    IUpdateInstallHandoff installHandoff) : IDesktopUpdateCoordinator
{
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object stateSync = new();
    private DesktopUpdatePhase phase = DesktopUpdatePhase.Idle;
    private UpdateInfo? latestRelease;
    private PreparedUpdate? preparedUpdate;
    private UpdatePreparationStage? preparationStage;
    private long bytesReceived;
    private long? totalBytes;
    private DateTimeOffset? lastCheckedAtUtc;
    private string? errorCode;
    private string? errorMessage;

    public DesktopUpdateSnapshot GetState()
    {
        lock (stateSync)
        {
            var busy = phase is DesktopUpdatePhase.Checking or DesktopUpdatePhase.Downloading or DesktopUpdatePhase.Verifying or DesktopUpdatePhase.Installing;
            return new DesktopUpdateSnapshot(
                phase, versionProvider.CurrentVersion, latestRelease, preparationStage, bytesReceived, totalBytes, lastCheckedAtUtc, errorCode, errorMessage,
                !busy && preparedUpdate is null && phase is DesktopUpdatePhase.Idle or DesktopUpdatePhase.UpToDate or DesktopUpdatePhase.Available or DesktopUpdatePhase.Error,
                !busy && latestRelease is not null && preparedUpdate is null && phase is DesktopUpdatePhase.Available or DesktopUpdatePhase.Error,
                !busy && preparedUpdate is not null && phase is DesktopUpdatePhase.Ready or DesktopUpdatePhase.Error);
        }
    }

    public ValueTask<DesktopUpdateSnapshot> CheckAsync(UpdateCheckTrigger trigger, CancellationToken cancellationToken = default) => ValueTask.FromResult(GetState());
    public ValueTask<DesktopUpdateSnapshot> DownloadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(GetState());
    public ValueTask<DesktopUpdateSnapshot> InstallAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(GetState());
}
