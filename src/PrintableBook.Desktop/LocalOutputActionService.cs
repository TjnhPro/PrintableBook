using System.Diagnostics;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using System.Windows;

namespace PrintableBook.Desktop;

internal sealed class LocalOutputActionService : ILocalOutputActionService
{
    public ValueTask OpenAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(file.Value) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }

    public ValueTask RevealAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.Value}\"") { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }

    public ValueTask CopyPathAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clipboard.SetText(file.Value);
        return ValueTask.CompletedTask;
    }
}
