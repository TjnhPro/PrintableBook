using System.Diagnostics;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using System.Windows;

namespace PrintableBook.Desktop;

internal sealed class LocalOutputActionService : ILocalOutputActionService
{
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public LocalOutputActionService() : this(Process.Start)
    {
    }

    internal LocalOutputActionService(Func<ProcessStartInfo, Process?> startProcess)
    {
        this.startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public ValueTask OpenAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        startProcess(new ProcessStartInfo(file.Value) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenFolderAsync(DirectoryReference directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        startProcess(new ProcessStartInfo(directory.Value) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }

    public ValueTask RevealAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        startProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{file.Value}\"") { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }

    public ValueTask CopyPathAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clipboard.SetText(file.Value);
        return ValueTask.CompletedTask;
    }
}
