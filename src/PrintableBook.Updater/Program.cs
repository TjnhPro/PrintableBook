namespace PrintableBook.Updater;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (!UpdaterCommandParser.TryParse(args, out var command, out _))
            return (int)UpdaterExitCode.InvalidArguments;

        var workerExecutablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
            return (int)UpdaterExitCode.PreflightFailed;

        try { new UpdaterPathPolicy().Validate(command!, workerExecutablePath); }
        catch { return (int)UpdaterExitCode.PreflightFailed; }

        var logFilePath = Path.Combine(command!.UpdatesRoot, "logs", $"updater-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        var logger = new FileUpdaterLogger(logFilePath);
        var payloadValidator = new UpdaterPayloadContractValidator();
        var engine = new UpdaterEngine(
            payloadValidator,
            new ProcessWaiter(),
            new UpdaterBackupService(payloadValidator),
            new UpdaterPayloadInstaller(payloadValidator),
            new ApplicationRestarter(),
            logger);

        try { return (int)await engine.RunAsync(command); }
        catch (Exception exception)
        {
            logger.Error("Unexpected updater failure.", exception);
            return (int)UpdaterExitCode.UnexpectedFailure;
        }
    }
}
