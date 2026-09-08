using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace PrintableBook.Desktop.Updates;

public sealed class UpdaterProcessLauncher : IUpdaterProcessLauncher
{
    public void Launch(UpdaterLaunchRequest request)
    {
        using var process = Process.Start(CreateStartInfo(request)) ?? throw new InvalidOperationException("Could not start PrintableBook updater.");
    }

    internal static ProcessStartInfo CreateStartInfo(UpdaterLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WaitPid <= 0 || !Path.IsPathFullyQualified(request.AppRoot) || !Path.IsPathFullyQualified(request.PayloadDirectory) || !Path.IsPathFullyQualified(request.UpdatesRoot) || request.CurrentVersion.Build < 0)
            throw new ArgumentException("Updater launch request is invalid.", nameof(request));
        var updaterPath = Path.Combine(request.PayloadDirectory, "PrintableBook.Updater.exe");
        if (!File.Exists(updaterPath)) throw new FileNotFoundException("Staged updater executable is missing.", updaterPath);
        var startInfo = new ProcessStartInfo { FileName = updaterPath, WorkingDirectory = request.PayloadDirectory, UseShellExecute = false };
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(request.WaitPid.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--app-root");
        startInfo.ArgumentList.Add(request.AppRoot);
        startInfo.ArgumentList.Add("--payload-dir");
        startInfo.ArgumentList.Add(request.PayloadDirectory);
        startInfo.ArgumentList.Add("--updates-root");
        startInfo.ArgumentList.Add(request.UpdatesRoot);
        startInfo.ArgumentList.Add("--current-version");
        startInfo.ArgumentList.Add(request.CurrentVersion.ToString(3));
        return startInfo;
    }
}
