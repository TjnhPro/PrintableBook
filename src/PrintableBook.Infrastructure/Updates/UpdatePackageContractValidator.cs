namespace PrintableBook.Infrastructure.Updates;

public sealed class UpdatePackageContractValidator
{
    private static readonly string[] RequiredFiles =
    [
        "PrintableBook.exe",
        "PrintableBook.Updater.exe",
        Path.Combine("Frontend", "index.html"),
        Path.Combine("Frontend", "js", "app.js"),
        Path.Combine("Frontend", "assets", "printable-book-logo.png"),
    ];

    private static readonly string[] RequiredDirectories =
    [
        "Frontend",
        Path.Combine("Frontend", "css"),
        Path.Combine("Frontend", "js"),
        Path.Combine("Frontend", "assets"),
    ];

    private static readonly HashSet<string> AllowedRootEntries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PrintableBook.exe",
            "PrintableBook.Updater.exe",
            "Frontend",
        };

    public void Validate(string payloadDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDirectoryPath);

        if (!Directory.Exists(payloadDirectoryPath))
        {
            throw new InvalidDataException("Update payload directory is missing.");
        }

        foreach (var relativePath in RequiredDirectories)
        {
            if (!Directory.Exists(Path.Combine(payloadDirectoryPath, relativePath)))
            {
                throw new InvalidDataException($"Update payload is missing required directory '{relativePath}'.");
            }
        }

        foreach (var relativePath in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(payloadDirectoryPath, relativePath)))
            {
                throw new InvalidDataException($"Update payload is missing required file '{relativePath}'.");
            }
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(payloadDirectoryPath))
        {
            var name = Path.GetFileName(entry);
            if (!AllowedRootEntries.Contains(name))
            {
                throw new InvalidDataException($"Update payload contains unexpected root entry '{name}'.");
            }
        }
    }
}
