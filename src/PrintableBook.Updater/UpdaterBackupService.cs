namespace PrintableBook.Updater;

public sealed class UpdaterBackupService(UpdaterPayloadContractValidator payloadValidator) : IUpdaterBackupService
{
    private static readonly string[] ControlledFiles = ["PrintableBook.exe", "PrintableBook.Updater.exe"];
    private const string FrontendDirectory = "Frontend";

    public void CreateBackup(string appRoot, string backupDirectory)
    {
        payloadValidator.ValidateInstalledPayload(appRoot);
        var temporary = backupDirectory + ".building-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyControlledPayload(appRoot, temporary);
            payloadValidator.ValidateStagedPayload(temporary);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
            Directory.Move(temporary, backupDirectory);
        }
        catch
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            throw;
        }
    }

    public void RestoreBackup(string backupDirectory, string appRoot)
    {
        payloadValidator.ValidateStagedPayload(backupDirectory);
        Directory.CreateDirectory(appRoot);
        foreach (var file in ControlledFiles)
        {
            var destination = Path.Combine(appRoot, file);
            if (File.Exists(destination)) File.Delete(destination);
        }

        var frontend = Path.Combine(appRoot, FrontendDirectory);
        if (Directory.Exists(frontend)) Directory.Delete(frontend, recursive: true);
        CopyControlledPayload(backupDirectory, appRoot);
        payloadValidator.ValidateInstalledPayload(appRoot);
    }

    internal static void CopyControlledPayload(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in ControlledFiles)
            File.Copy(Path.Combine(source, file), Path.Combine(destination, file), overwrite: true);
        CopyDirectory(Path.Combine(source, FrontendDirectory), Path.Combine(destination, FrontendDirectory));
    }

    internal static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
