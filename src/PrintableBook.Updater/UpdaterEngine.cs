using System.ComponentModel;

namespace PrintableBook.Updater;

public sealed class UpdaterEngine(
    UpdaterPayloadContractValidator payloadValidator,
    IProcessWaiter processWaiter,
    IUpdaterBackupService backupService,
    IUpdaterPayloadInstaller installer,
    IApplicationRestarter restarter,
    IUpdaterLogger logger)
{
    public static readonly TimeSpan MainProcessWaitTimeout = TimeSpan.FromSeconds(30);

    public async ValueTask<UpdaterExitCode> RunAsync(UpdaterCommand command, CancellationToken cancellationToken = default)
    {
        TryLogInfo("Waiting for PrintableBook to exit.");
        if (!await processWaiter.WaitForExitAsync(command.WaitPid, MainProcessWaitTimeout, cancellationToken))
            return UpdaterExitCode.WaitTimeout;

        try
        {
            payloadValidator.ValidateStagedPayload(command.PayloadDirectory);
            payloadValidator.ValidateInstalledPayload(command.AppRoot);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            TryLogError("Updater preflight failed.", exception);
            TryRestartUnchangedApplication(command.AppRoot);
            return UpdaterExitCode.PreflightFailed;
        }

        try { backupService.CreateBackup(command.AppRoot, command.BackupDirectory); }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            TryLogError("Creating backup failed.", exception);
            TryRestartUnchangedApplication(command.AppRoot);
            return UpdaterExitCode.BackupFailed;
        }

        try { installer.Install(command.PayloadDirectory, command.AppRoot); }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            TryLogError("Installing payload failed.", exception);
            try { backupService.RestoreBackup(command.BackupDirectory, command.AppRoot); }
            catch (Exception rollbackException) when (IsExpectedFailure(rollbackException))
            {
                TryLogError("Rollback failed.", rollbackException);
                return UpdaterExitCode.RollbackFailed;
            }
            TryRestartUnchangedApplication(command.AppRoot);
            return UpdaterExitCode.InstallFailedRolledBack;
        }

        try
        {
            restarter.Restart(command.AppRoot);
            return UpdaterExitCode.Success;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            TryLogError("Restarting updated application failed.", exception);
            return UpdaterExitCode.RestartFailed;
        }
    }

    private void TryRestartUnchangedApplication(string appRoot)
    {
        try { restarter.Restart(appRoot); }
        catch (Exception exception) when (IsExpectedFailure(exception)) { TryLogError("Restarting unchanged application failed.", exception); }
    }

    private void TryLogInfo(string message)
    {
        try { logger.Info(message); }
        catch
        {
            // Logging is best-effort and must never interrupt updater state transitions.
        }
    }

    private void TryLogError(string message, Exception exception)
    {
        try { logger.Error(message, exception); }
        catch
        {
            // Logging is best-effort and must never interrupt updater state transitions.
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or Win32Exception;
}
