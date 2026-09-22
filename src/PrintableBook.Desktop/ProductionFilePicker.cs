using Microsoft.Win32;
using PrintableBook.Core.Abstractions;

namespace PrintableBook.Desktop;

public interface IProductionFilePicker
{
    ValueTask<FileReference?> PickPngAsync(
        string assetLabel,
        DirectoryReference initialDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionFilePicker : IProductionFilePicker
{
    public ValueTask<FileReference?> PickPngAsync(
        string assetLabel,
        DirectoryReference initialDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFileDialog
        {
            Title = $"Select {assetLabel} PNG",
            Filter = "PNG image (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            InitialDirectory = initialDirectory.Value
        };
        return ValueTask.FromResult(dialog.ShowDialog() == true
            ? new FileReference(dialog.FileName)
            : null);
    }
}
