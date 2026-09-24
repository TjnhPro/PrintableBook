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
        if (Process.Start(new ProcessStartInfo(file.Value) { UseShellExecute = true }) is null)
        {
            throw new InvalidOperationException($"Windows could not open '{file.Value}'.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenFolderAsync(DirectoryReference directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Process.Start(new ProcessStartInfo(directory.Value) { UseShellExecute = true }) is null)
        {
            throw new InvalidOperationException($"Windows could not open '{directory.Value}'.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask RevealAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.Value}\"") { UseShellExecute = true }) is null)
        {
            throw new InvalidOperationException($"Windows could not reveal '{file.Value}'.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask CopyPathAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clipboard.SetText(file.Value);
        return ValueTask.CompletedTask;
    }
}
