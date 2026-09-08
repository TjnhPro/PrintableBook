namespace PrintableBook.Updater;

public sealed class UpdaterPayloadInstaller(UpdaterPayloadContractValidator payloadValidator) : IUpdaterPayloadInstaller
{
    public void Install(string payloadDirectory, string appRoot)
    {
        payloadValidator.ValidateStagedPayload(payloadDirectory);
        Directory.CreateDirectory(appRoot);
        var frontend = Path.Combine(appRoot, "Frontend");
        if (Directory.Exists(frontend)) Directory.Delete(frontend, recursive: true);
        UpdaterBackupService.CopyDirectory(Path.Combine(payloadDirectory, "Frontend"), frontend);
        File.Copy(Path.Combine(payloadDirectory, "PrintableBook.exe"), Path.Combine(appRoot, "PrintableBook.exe"), overwrite: true);
        File.Copy(Path.Combine(payloadDirectory, "PrintableBook.Updater.exe"), Path.Combine(appRoot, "PrintableBook.Updater.exe"), overwrite: true);
        payloadValidator.ValidateInstalledPayload(appRoot);
    }
}
