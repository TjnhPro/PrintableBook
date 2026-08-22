namespace PrintableBook.Core.Abstractions;

public interface IFileSystem
{
    ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default);

    IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(
        DirectoryReference directory,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileReference> EnumerateFilesAsync(
        DirectoryReference directory,
        CancellationToken cancellationToken = default);
}
