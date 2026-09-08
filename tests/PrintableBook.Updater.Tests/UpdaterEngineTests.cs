using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class UpdaterEngineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false, false, false, UpdaterExitCode.Success, "wait,backup,install,restart")]
    [InlineData(false, false, false, true, UpdaterExitCode.RestartFailed, "wait,backup,install,restart")]
    [InlineData(false, true, false, false, UpdaterExitCode.BackupFailed, "wait,backup,restart")]
    [InlineData(true, false, false, false, UpdaterExitCode.WaitTimeout, "wait")]
    public async Task RunAsyncMapsNormalBranches(bool timeout, bool backupFails, bool installFails, bool restartFails, UpdaterExitCode expected, string order)
    {
        var recording = new List<string>();
        var result = await CreateEngine(recording, timeout, backupFails, installFails, false, restartFails).RunAsync(CreateCommand());
        Assert.Equal(expected, result);
        Assert.Equal(order, string.Join(',', recording));
    }

    [Fact]
    public void FileLoggerWritesUtcInfoAndErrorLines()
    {
        var log = Path.Combine(root, "Updates", "logs", "updater.log");
        var logger = new FileUpdaterLogger(log);
        logger.Info("started");
        logger.Error("failed", new IOException("locked"));
        var content = File.ReadAllText(log);
        Assert.Contains("[INFO] started", content);
        Assert.Contains("[ERROR] failed | System.IO.IOException: locked", content);
    }

    [Fact]
    public async Task RunAsyncRollsBackAndRestartsOldAppAfterInstallFailure()
    {
        var recording = new List<string>();
        var result = await CreateEngine(recording, false, false, true, false, false).RunAsync(CreateCommand());
        Assert.Equal(UpdaterExitCode.InstallFailedRolledBack, result);
        Assert.Equal("wait,backup,install,restore,restart", string.Join(',', recording));
    }

    [Fact]
    public async Task RunAsyncDoesNotRestartAfterRollbackFailure()
    {
        var recording = new List<string>();
        var result = await CreateEngine(recording, false, false, true, true, false).RunAsync(CreateCommand());
        Assert.Equal(UpdaterExitCode.RollbackFailed, result);
        Assert.Equal("wait,backup,install,restore", string.Join(',', recording));
    }

    private UpdaterEngine CreateEngine(List<string> calls, bool timeout, bool backupFails, bool installFails, bool rollbackFails, bool restartFails) => new(
        new UpdaterPayloadContractValidator(), new Waiter(calls, timeout), new Backup(calls, backupFails, rollbackFails), new Installer(calls, installFails), new Restarter(calls, restartFails), new Logger());

    private UpdaterCommand CreateCommand()
    {
        var app = Path.Combine(root, "App"); var payload = Path.Combine(root, "Payload");
        UpdaterPayloadContractValidatorTests.CreateValidPayload(app); UpdaterPayloadContractValidatorTests.CreateValidPayload(payload);
        return new UpdaterCommand(1, app, payload, Path.Combine(root, "Updates"), new Version(0, 2, 0));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Waiter(List<string> calls, bool timeout) : IProcessWaiter { public ValueTask<bool> WaitForExitAsync(int _, TimeSpan __, CancellationToken ___ = default) { calls.Add("wait"); return ValueTask.FromResult(!timeout); } }
    private sealed class Backup(List<string> calls, bool fails, bool restoreFails) : IUpdaterBackupService { public void CreateBackup(string _, string __) { calls.Add("backup"); if (fails) throw new IOException(); } public void RestoreBackup(string _, string __) { calls.Add("restore"); if (restoreFails) throw new IOException(); } }
    private sealed class Installer(List<string> calls, bool fails) : IUpdaterPayloadInstaller { public void Install(string _, string __) { calls.Add("install"); if (fails) throw new IOException(); } }
    private sealed class Restarter(List<string> calls, bool fails) : IApplicationRestarter { public void Restart(string _) { calls.Add("restart"); if (fails) throw new InvalidOperationException(); } }
    private sealed class Logger : IUpdaterLogger { public void Error(string _, Exception __) { } public void Info(string _) { } }
}
