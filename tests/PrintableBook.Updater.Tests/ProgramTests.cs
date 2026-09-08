using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class ProgramTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MainReturnsUnexpectedFailureWhenLoggerInitializationFails()
    {
        var args = CreateValidArguments(out var payload);
        Program.ProcessPathProvider = () => Path.Combine(payload, "PrintableBook.Updater.exe");
        Program.LoggerFactory = _ => throw new IOException("Simulated log initialization failure.");

        var exitCode = await Program.Main(args);

        Assert.Equal((int)UpdaterExitCode.UnexpectedFailure, exitCode);
    }

    [Fact]
    public void TryLogUnexpectedFailureDoesNotThrow()
    {
        var exception = Record.Exception(() => Program.TryLogUnexpectedFailure(new ThrowingLogger(), new InvalidOperationException("engine failure")));
        Assert.Null(exception);
    }

    public void Dispose()
    {
        Program.ResetTestHooks();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private string[] CreateValidArguments(out string payload)
    {
        var appRoot = Path.Combine(root, "App");
        var updatesRoot = Path.Combine(root, "Updates");
        payload = Path.Combine(updatesRoot, "staging", "0.2.1", "payload");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(payload);
        return
        [
            "--wait-pid", Environment.ProcessId.ToString(),
            "--app-root", appRoot,
            "--payload-dir", payload,
            "--updates-root", updatesRoot,
            "--current-version", "0.2.0",
        ];
    }

    private sealed class ThrowingLogger : IUpdaterLogger
    {
        public void Info(string _) => throw new IOException("Simulated info logging failure.");
        public void Error(string _, Exception __) => throw new IOException("Simulated error logging failure.");
    }
}
