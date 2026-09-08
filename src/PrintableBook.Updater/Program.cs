namespace PrintableBook.Updater;

public static class Program
{
    internal static Func<string?> ProcessPathProvider { get; set; } = static () => Environment.ProcessPath;

    internal static Func<string, IUpdaterLogger> LoggerFactory { get; set; } = static path => new FileUpdaterLogger(path);

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (!UpdaterCommandParser.TryParse(args, out var command, out _))
            return (int)UpdaterExitCode.InvalidArguments;

        var workerExecutablePath = ProcessPathProvider();
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
            return (int)UpdaterExitCode.PreflightFailed;

        try { new UpdaterPathPolicy().Validate(command!, workerExecutablePath); }
        catch { return (int)UpdaterExitCode.PreflightFailed; }

        IUpdaterLogger logger;
        try
        {
            var logFilePath = Path.Combine(command!.UpdatesRoot, "logs", $"updater-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
            logger = LoggerFactory(logFilePath);
        }
        catch
        {
            return (int)UpdaterExitCode.UnexpectedFailure;
        }
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
            TryLogUnexpectedFailure(logger, exception);
            return (int)UpdaterExitCode.UnexpectedFailure;
        }
    }

    internal static void TryLogUnexpectedFailure(IUpdaterLogger logger, Exception exception)
    {
        try { logger.Error("Unexpected updater failure.", exception); }
        catch
        {
            // Logging is best-effort even at the process boundary.
        }
    }

    internal static void ResetTestHooks()
    {
        ProcessPathProvider = static () => Environment.ProcessPath;
        LoggerFactory = static path => new FileUpdaterLogger(path);
    }
}
