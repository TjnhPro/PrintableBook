using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Storage;

namespace PrintableBook.Infrastructure.Workspaces;

public sealed class PhysicalBookStorageMaintenance : IBookStorageMaintenance
{
    private const string LegacyStampSuffix = ".input-stamp.json";
    private static readonly HashSet<string> PageCacheMetadataFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "classification.json",
        "input-stamp.json"
    };

    public ValueTask<long> ClearHeavyProcessingCacheAsync(
        BookWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        long freedBytes = 0;
        try
        {
            var cacheRoot = Path.Combine(workspace.WorkingDirectory.Value, "cache");
            var processedInterior = Path.Combine(workspace.ProcessedDirectory.Value, "interior");
            MigrateLegacyStamps(cacheRoot, processedInterior, cancellationToken);
            DeletePageCacheArtifacts(cacheRoot, ref freedBytes, cancellationToken);
            DeleteDirectoryContents(processedInterior, ref freedBytes, cancellationToken);
            DeleteDirectoryContents(workspace.TemporaryOutputDirectory.Value, ref freedBytes, cancellationToken);
            return ValueTask.FromResult(freedBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BookStorageCleanupException(freedBytes, exception);
        }
    }

    private static void MigrateLegacyStamps(string cacheRoot, string processedInterior, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(processedInterior)) return;
        foreach (var legacyStamp in Directory.EnumerateFiles(processedInterior, $"*{LegacyStampSuffix}", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageId = Path.GetFileName(legacyStamp)[..^LegacyStampSuffix.Length];
            var pageCache = Path.Combine(cacheRoot, pageId);
            var destination = Path.Combine(pageCache, "input-stamp.json");
            Directory.CreateDirectory(pageCache);
            if (File.Exists(destination))
            {
                File.Delete(legacyStamp);
                continue;
            }

            File.Move(legacyStamp, destination);
        }
    }

    private static void DeletePageCacheArtifacts(string cacheRoot, ref long freedBytes, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(cacheRoot)) return;
        foreach (var pageDirectory in Directory.EnumerateDirectories(cacheRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(pageDirectory)) continue;
            DeletePageCacheArtifactsTree(pageDirectory, ref freedBytes, cancellationToken);
        }
    }

    private static void DeletePageCacheArtifactsTree(string directory, ref long freedBytes, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PageCacheMetadataFileNames.Contains(Path.GetFileName(file)))
            {
                DeleteFileAndCount(file, ref freedBytes);
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsReparsePoint(child)) DeletePageCacheArtifactsTree(child, ref freedBytes, cancellationToken);
        }
    }

    private static void DeleteDirectoryContents(string directory, ref long freedBytes, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFileAndCount(file, ref freedBytes);
        }

        foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(child))
            {
                Directory.Delete(child, recursive: false);
                continue;
            }

            DeleteDirectoryTree(child, ref freedBytes, cancellationToken);
        }
    }

    private static void DeleteDirectoryTree(string directory, ref long freedBytes, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFileAndCount(file, ref freedBytes);
        }

        foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(child))
            {
                Directory.Delete(child, recursive: false);
                continue;
            }

            DeleteDirectoryTree(child, ref freedBytes, cancellationToken);
        }

        Directory.Delete(directory, recursive: false);
    }

    private static void DeleteFileAndCount(string file, ref long freedBytes)
    {
        if (!File.Exists(file)) return;
        var length = new FileInfo(file).Length;
        File.Delete(file);
        if (!File.Exists(file)) freedBytes += length;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
