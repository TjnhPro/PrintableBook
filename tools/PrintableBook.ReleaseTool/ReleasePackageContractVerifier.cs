using System.IO.Compression;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool;

internal sealed class ReleasePackageContractVerifier
{
    private static readonly HashSet<string> RequiredFiles = new(StringComparer.Ordinal)
    {
        "PrintableBook.exe",
        "PrintableBook.Updater.exe",
        "Frontend/index.html",
        "Frontend/js/app.js",
        "Frontend/assets/printable-book-logo.png"
    };

    public void Verify(string archivePath, Version version, string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var wrapper = $"PrintableBook-{version.ToString(3)}-{runtimeIdentifier}";
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.Select(entry => new PackageEntry(Normalize(entry.FullName), entry.FullName.EndsWith("/", StringComparison.Ordinal))).ToArray();
        var roots = entries.Select(entry => entry.Path.Split('/')[0]).Distinct(StringComparer.Ordinal).ToArray();
        var wrapped = roots.Contains(wrapper, StringComparer.Ordinal);
        if (wrapped && roots.Length != 1) throw new InvalidDataException("Release package has multiple root directories.");

        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (wrapped && entry.IsDirectory && entry.Path == wrapper) continue;
            var path = wrapped ? entry.Path[(wrapper.Length + 1)..] : entry.Path;
            var root = path.Split('/')[0];
            if (root is not ("PrintableBook.exe" or "PrintableBook.Updater.exe" or "Frontend"))
            {
                throw new InvalidDataException("Release package contains an unexpected root entry.");
            }

            if (!entry.IsDirectory) files.Add(path);
        }

        if (!RequiredFiles.IsSubsetOf(files)) throw new InvalidDataException("Release package is missing required application files.");
    }

    private static string Normalize(string entryName)
    {
        var value = entryName.Replace('\\', '/');
        if (value.EndsWith("/", StringComparison.Ordinal)) value = value[..^1];
        if (value.StartsWith("/", StringComparison.Ordinal) || value.Length == 0) throw new InvalidDataException("Release package contains a rooted entry.");
        var segments = value.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment == "..") || segments[0].EndsWith(':'))
        {
            throw new InvalidDataException("Release package contains an unsafe entry.");
        }

        return value;
    }

    private sealed record PackageEntry(string Path, bool IsDirectory);
}
