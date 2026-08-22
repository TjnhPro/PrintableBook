namespace PrintableBook.Core.Abstractions;

public interface IFileSystem
{
    ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default);

    ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default);

    ValueTask CreateDirectoryAsync(DirectoryReference directory, CancellationToken cancellationToken = default);

    IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(
        DirectoryReference directory,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileReference> EnumerateFilesAsync(
        DirectoryReference directory,
        CancellationToken cancellationToken = default);

    ValueTask<string> ReadTextAsync(FileReference file, CancellationToken cancellationToken = default);

    ValueTask WriteTextAtomicallyAsync(FileReference file, string content, CancellationToken cancellationToken = default);

    ValueTask CopyFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default);

    ValueTask MoveFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default);

    ValueTask DeleteFileAsync(FileReference file, CancellationToken cancellationToken = default);

    ValueTask DeleteDirectoryAsync(DirectoryReference directory, bool recursive, CancellationToken cancellationToken = default);
}
