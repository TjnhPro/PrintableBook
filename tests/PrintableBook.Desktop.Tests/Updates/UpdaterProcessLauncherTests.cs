using PrintableBook.Desktop.Updates;

namespace PrintableBook.Desktop.Tests.Updates;

public sealed class UpdaterProcessLauncherTests
{
    [Fact]
    public void Start_info_uses_the_staged_updater_and_the_exact_sidecar_arguments()
    {
        var payloadDirectory = Path.Combine(Path.GetTempPath(), $"PrintableBook.UpdaterTests.{Guid.NewGuid():N}", "payload with spaces");
        Directory.CreateDirectory(payloadDirectory);
        var updaterPath = Path.Combine(payloadDirectory, "PrintableBook.Updater.exe");
        File.WriteAllText(updaterPath, string.Empty);

        try
        {
            var startInfo = UpdaterProcessLauncher.CreateStartInfo(new UpdaterLaunchRequest(
                1234,
                Path.GetFullPath(Path.Combine(payloadDirectory, "..", "app")),
                payloadDirectory,
                Path.GetFullPath(Path.Combine(payloadDirectory, "..", "updates")),
                new Version(0, 1, 1)));

            Assert.Equal(updaterPath, startInfo.FileName);
            Assert.Equal(payloadDirectory, startInfo.WorkingDirectory);
            Assert.False(startInfo.UseShellExecute);
            Assert.Equal(
                ["--wait-pid", "1234", "--app-root", Path.GetFullPath(Path.Combine(payloadDirectory, "..", "app")), "--payload-dir", payloadDirectory, "--updates-root", Path.GetFullPath(Path.Combine(payloadDirectory, "..", "updates")), "--current-version", "0.1.1"],
                startInfo.ArgumentList);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(payloadDirectory)!, recursive: true);
        }
    }

    [Fact]
    public void Start_info_requires_the_staged_updater_executable()
    {
        var payloadDirectory = Path.Combine(Path.GetTempPath(), $"PrintableBook.UpdaterTests.{Guid.NewGuid():N}", "payload");
        Directory.CreateDirectory(payloadDirectory);

        try
        {
            Assert.Throws<FileNotFoundException>(() => UpdaterProcessLauncher.CreateStartInfo(new UpdaterLaunchRequest(
                1234,
                Path.GetFullPath(Path.Combine(payloadDirectory, "..", "app")),
                payloadDirectory,
                Path.GetFullPath(Path.Combine(payloadDirectory, "..", "updates")),
                new Version(0, 1, 1))));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(payloadDirectory)!, recursive: true);
        }
    }

    [Fact]
    public void Runtime_info_exposes_the_current_process_and_an_absolute_app_root()
    {
        var runtimeInfo = new UpdateRuntimeInfo();

        Assert.Equal(Environment.ProcessId, runtimeInfo.ProcessId);
        Assert.True(Path.IsPathFullyQualified(runtimeInfo.AppRoot));
    }
}
