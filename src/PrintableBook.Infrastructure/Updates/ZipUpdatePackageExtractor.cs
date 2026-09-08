using System.IO.Compression;

namespace PrintableBook.Infrastructure.Updates;

public sealed class ZipUpdatePackageExtractor
{
    public async ValueTask<string> ExtractAsync(
        string archivePath,
        string extractionDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(extractionDirectory);
        var rootFullPath = Path.GetFullPath(extractionDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        try
        {
            await using var stream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = Path.GetFullPath(
                    Path.Combine(extractionDirectory, entry.FullName));

                if (!destinationPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"ZIP entry escapes extraction directory: '{entry.FullName}'.");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                if (File.Exists(destinationPath))
                {
                    throw new InvalidDataException(
                        $"ZIP archive contains duplicate file destination: '{entry.FullName}'.");
                }

                await using var source = entry.Open();
                await using var destination = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                await source.CopyToAsync(destination, cancellationToken);
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("Update package is not a valid ZIP archive.", exception);
        }

        return DetectPayloadRoot(extractionDirectory);
    }

    private static string DetectPayloadRoot(string extractionDirectory)
    {
        if (File.Exists(Path.Combine(extractionDirectory, "PrintableBook.exe")))
        {
            return extractionDirectory;
        }

        var entries = Directory.EnumerateFileSystemEntries(extractionDirectory).ToArray();
        var directories = entries.Where(Directory.Exists).ToArray();
        var files = entries.Where(File.Exists).ToArray();
        if (files.Length != 0 || directories.Length != 1 ||
            !File.Exists(Path.Combine(directories[0], "PrintableBook.exe")))
        {
            throw new InvalidDataException("Update package has an ambiguous or missing payload root.");
        }

        return directories[0];
    }
}
