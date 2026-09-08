using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Desktop.Updates;

public sealed class DesktopUpdateCoordinator(
    IUpdateService updateService,
    IUpdatePreparationService preparationService,
    IApplicationVersionProvider versionProvider,
    IUpdateInstallHandoff installHandoff) : IDesktopUpdateCoordinator
{
    private const string CheckFailureCode = "update_check_failed";
    private const string DownloadFailureCode = "update_download_failed";
    private const string InstallFailureCode = "update_install_failed";
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

    public async ValueTask<DesktopUpdateSnapshot> CheckAsync(UpdateCheckTrigger trigger, CancellationToken cancellationToken = default)
    {
        if (!await TryEnterOperationAsync(cancellationToken)) return GetState();
        var previous = GetState();
        try
        {
            SetState(DesktopUpdatePhase.Checking, null, null, null, 0, null);
            var result = await updateService.CheckAsync(cancellationToken);
            lock (stateSync)
            {
                latestRelease = result.Availability == UpdateAvailability.Available ? result.LatestRelease : null;
                preparedUpdate = null;
                preparationStage = null;
                bytesReceived = 0;
                totalBytes = null;
                lastCheckedAtUtc = DateTimeOffset.UtcNow;
                errorCode = null;
                errorMessage = null;
                phase = result.Availability == UpdateAvailability.Available ? DesktopUpdatePhase.Available : DesktopUpdatePhase.UpToDate;
            }
            return GetState();
        }
        catch (OperationCanceledException)
        {
            Restore(previous);
            throw;
        }
        catch (Exception)
        {
            if (trigger == UpdateCheckTrigger.Automatic) Restore(previous);
            else SetState(DesktopUpdatePhase.Error, CheckFailureCode, "Could not check for updates. Check your internet connection and try again.");
            return GetState();
        }
        finally { operationGate.Release(); }
    }

    public async ValueTask<DesktopUpdateSnapshot> DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!await TryEnterOperationAsync(cancellationToken)) return GetState();
        var previous = GetState();
        try
        {
            UpdateInfo update;
            lock (stateSync)
            {
                if (latestRelease is null || preparedUpdate is not null || phase is not (DesktopUpdatePhase.Available or DesktopUpdatePhase.Error)) return GetState();
                update = latestRelease;
            }
            StartPreparation();
            var result = await preparationService.PrepareAsync(update, new DelegateProgress<UpdatePreparationProgress>(ApplyPreparationProgress), cancellationToken);
            lock (stateSync)
            {
                preparedUpdate = result;
                preparationStage = UpdatePreparationStage.Ready;
                phase = DesktopUpdatePhase.Ready;
                errorCode = null;
                errorMessage = null;
            }
            return GetState();
        }
        catch (OperationCanceledException)
        {
            Restore(previous);
            throw;
        }
        catch (Exception)
        {
            lock (stateSync) { phase = DesktopUpdatePhase.Error; preparedUpdate = null; errorCode = DownloadFailureCode; errorMessage = "Could not download or verify the update. Try again."; }
            return GetState();
        }
        finally { operationGate.Release(); }
    }

    public async ValueTask<DesktopUpdateSnapshot> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!await TryEnterOperationAsync(cancellationToken)) return GetState();
        var previous = GetState();
        try
        {
            PreparedUpdate update;
            lock (stateSync)
            {
                if (preparedUpdate is null || phase is not (DesktopUpdatePhase.Ready or DesktopUpdatePhase.Error)) return GetState();
                update = preparedUpdate;
            }
            SetState(DesktopUpdatePhase.Installing, null, null);
            var outcome = await installHandoff.BeginAsync(update, cancellationToken);
            if (outcome == UpdateInstallHandoffOutcome.Cancelled) SetState(DesktopUpdatePhase.Ready, null, null);
            return GetState();
        }
        catch (OperationCanceledException)
        {
            Restore(previous);
            throw;
        }
        catch (Exception)
        {
            SetState(DesktopUpdatePhase.Error, InstallFailureCode, "Could not start the updater. Printable Book remains open and the prepared update can be retried.");
            return GetState();
        }
        finally { operationGate.Release(); }
    }

    private async ValueTask<bool> TryEnterOperationAsync(CancellationToken cancellationToken) => await operationGate.WaitAsync(TimeSpan.Zero, cancellationToken);

    private void ApplyPreparationProgress(UpdatePreparationProgress progress)
    {
        var mappedPhase = progress.Stage is UpdatePreparationStage.DownloadingArchive or UpdatePreparationStage.DownloadingChecksum ? DesktopUpdatePhase.Downloading : progress.Stage is UpdatePreparationStage.Ready ? DesktopUpdatePhase.Ready : DesktopUpdatePhase.Verifying;
        SetState(mappedPhase, null, null, progress.Stage, progress.BytesReceived, progress.TotalBytes);
    }

    private void StartPreparation()
    {
        lock (stateSync)
        {
            phase = DesktopUpdatePhase.Downloading;
            preparationStage = null;
            bytesReceived = 0;
            totalBytes = null;
            errorCode = null;
            errorMessage = null;
        }
    }

    private void SetState(DesktopUpdatePhase value, string? code, string? message, UpdatePreparationStage? stage = null, long? received = null, long? total = null)
    {
        lock (stateSync)
        {
            phase = value; errorCode = code; errorMessage = message;
            if (stage is not null) preparationStage = stage;
            if (received is not null) bytesReceived = received.Value;
            if (total is not null || received is not null) totalBytes = total;
        }
    }

    private void Restore(DesktopUpdateSnapshot snapshot)
    {
        lock (stateSync)
        {
            phase = snapshot.Phase; latestRelease = snapshot.LatestRelease; preparationStage = snapshot.PreparationStage; bytesReceived = snapshot.BytesReceived; totalBytes = snapshot.TotalBytes; lastCheckedAtUtc = snapshot.LastCheckedAtUtc; errorCode = snapshot.ErrorCode; errorMessage = snapshot.ErrorMessage;
        }
    }

    private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
