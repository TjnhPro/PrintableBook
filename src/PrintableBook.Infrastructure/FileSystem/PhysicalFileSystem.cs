using PrintableBook.Core.Abstractions;

namespace PrintableBook.Infrastructure.FileSystem;

public sealed class PhysicalFileSystem : IFileSystem
{
    public ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(File.Exists(file.Value));
    }

    public ValueTask<FileMetadata?> GetFileMetadataAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(file.Value);
        return ValueTask.FromResult(
            info.Exists
                ? new FileMetadata(info.Length, new DateTimeOffset(info.LastWriteTimeUtc))
                : (FileMetadata?)null);
    }

    public ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Directory.Exists(directory.Value));
    }

    public ValueTask CreateDirectoryAsync(DirectoryReference directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory.Value);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(
        DirectoryReference directory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var path in Directory.EnumerateDirectories(directory.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new DirectoryReference(path);
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<FileReference> EnumerateFilesAsync(
        DirectoryReference directory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var path in Directory.EnumerateFiles(directory.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new FileReference(path);
            await Task.Yield();
        }
    }

    public async ValueTask<string> ReadTextAsync(FileReference file, CancellationToken cancellationToken = default) =>
        await File.ReadAllTextAsync(file.Value, cancellationToken);

    public async ValueTask WriteTextAtomicallyAsync(FileReference file, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var directory = Path.GetDirectoryName(file.Value);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The file reference must include a directory.", nameof(file));
        }

        Directory.CreateDirectory(directory);
        var temporaryFile = Path.Combine(directory, $".{Path.GetFileName(file.Value)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryFile, content, cancellationToken);
            File.Move(temporaryFile, file.Value, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    public ValueTask CopyFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDestinationDirectory(destination);
        File.Copy(source.Value, destination.Value, overwrite);
        return ValueTask.CompletedTask;
    }

    public ValueTask MoveFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDestinationDirectory(destination);
        File.Move(source.Value, destination.Value, overwrite);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteFileAsync(FileReference file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(file.Value))
        {
            File.Delete(file.Value);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteDirectoryAsync(DirectoryReference directory, bool recursive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(directory.Value))
        {
            Directory.Delete(directory.Value, recursive);
        }

        return ValueTask.CompletedTask;
    }

    private static void EnsureDestinationDirectory(FileReference destination)
    {
        var directory = Path.GetDirectoryName(destination.Value);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The destination must include a directory.", nameof(destination));
        }

        Directory.CreateDirectory(directory);
    }
}
