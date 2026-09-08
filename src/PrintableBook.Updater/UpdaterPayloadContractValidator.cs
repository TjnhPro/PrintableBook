namespace PrintableBook.Updater;

public sealed class UpdaterPayloadContractValidator
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

    private static readonly HashSet<string> AllowedStagedRoot = new(StringComparer.OrdinalIgnoreCase)
    {
        "PrintableBook.exe", "PrintableBook.Updater.exe", "Frontend",
    };

    public void ValidateStagedPayload(string payloadDirectory)
    {
        ValidateRequirements(payloadDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(payloadDirectory))
        {
            if (!AllowedStagedRoot.Contains(Path.GetFileName(entry)))
            {
                throw new InvalidDataException("Staged payload contains an unsupported root entry.");
            }
        }
    }

    public void ValidateInstalledPayload(string appRoot) => ValidateRequirements(appRoot);

    private static void ValidateRequirements(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Payload root is missing.");
        foreach (var directory in RequiredDirectories)
        {
            if (!Directory.Exists(Path.Combine(root, directory))) throw new InvalidDataException($"Required directory is missing: {directory}");
        }
        foreach (var file in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(root, file))) throw new InvalidDataException($"Required file is missing: {file}");
        }
    }
}
