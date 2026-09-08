namespace PrintableBook.Updater;

public sealed class UpdaterPathPolicy
{
    public void Validate(UpdaterCommand command, string workerExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerExecutablePath);

        var appRoot = CanonicalDirectory(command.AppRoot);
        var updatesRoot = CanonicalDirectory(command.UpdatesRoot);
        var payloadDirectory = CanonicalDirectory(command.PayloadDirectory);
        var worker = CanonicalFile(workerExecutablePath);

        if (SameOrNested(appRoot, updatesRoot))
        {
            throw new ArgumentException("AppRoot and UpdatesRoot must be disjoint.");
        }

        var stagingRoot = Path.Combine(updatesRoot, "staging");
        if (!IsUnder(payloadDirectory, stagingRoot))
        {
            throw new ArgumentException("PayloadDirectory must be under UpdatesRoot\\staging.");
        }

        var expectedWorker = CanonicalFile(Path.Combine(payloadDirectory, "PrintableBook.Updater.exe"));
        if (!string.Equals(worker, expectedWorker, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Updater must execute from the staged payload.");
        }
    }

    private static bool SameOrNested(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
        IsUnder(first, second) ||
        IsUnder(second, first);

    private static string CanonicalDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Updater paths must be absolute.");
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string CanonicalFile(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Updater paths must be absolute.");
        }

        return Path.GetFullPath(path);
    }

    private static bool IsUnder(string candidate, string parent) =>
        candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
