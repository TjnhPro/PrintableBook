using System.Diagnostics;

namespace PrintableBook.Updater;

public sealed class ApplicationRestarter : IApplicationRestarter
{
    public void Restart(string appRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appRoot);
        var executable = Path.Combine(appRoot, "PrintableBook.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("PrintableBook executable is missing.", executable);
        using var process = Process.Start(new ProcessStartInfo { FileName = executable, WorkingDirectory = appRoot, UseShellExecute = true });
        if (process is null) throw new InvalidOperationException("Could not restart PrintableBook.");
    }
}
