using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class UpdaterBackupServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void BackupAndRestoreOnlyControlledPayload()
    {
        var app = Path.Combine(root, "App");
        var backup = Path.Combine(root, "Updates", "backup", "0.2.0");
        UpdaterPayloadContractValidatorTests.CreateValidPayload(app);
        File.WriteAllText(Path.Combine(app, "PrintableBook.exe"), "old-main");
        File.WriteAllText(Path.Combine(app, "settings.json"), "user-data");
        Directory.CreateDirectory(Path.Combine(app, ".cloakbrowser", "cache"));
        File.WriteAllText(Path.Combine(app, ".cloakbrowser", "cache", "browser.bin"), "runtime-data");
        var service = new UpdaterBackupService(new UpdaterPayloadContractValidator());

        service.CreateBackup(app, backup);
        Assert.Equal("old-main", File.ReadAllText(Path.Combine(backup, "PrintableBook.exe")));
        Assert.Equal("Metadata/cover_key.txt", File.ReadAllText(Path.Combine(backup, "Metadata", "cover_key.txt")));
        Assert.False(File.Exists(Path.Combine(backup, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(backup, ".cloakbrowser")));
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(backup)!, "*.building-*"));

        File.WriteAllText(Path.Combine(app, "PrintableBook.exe"), "broken");
        File.WriteAllText(Path.Combine(app, "Frontend", "index.html"), "broken");
        service.RestoreBackup(backup, app);
        Assert.Equal("old-main", File.ReadAllText(Path.Combine(app, "PrintableBook.exe")));
        Assert.Equal("user-data", File.ReadAllText(Path.Combine(app, "settings.json")));
        Assert.Equal("runtime-data", File.ReadAllText(Path.Combine(app, ".cloakbrowser", "cache", "browser.bin")));
    }

    [Fact]
    public void RestoreRejectsIncompleteBackupBeforeMutation()
    {
        var app = Path.Combine(root, "App");
        var backup = Path.Combine(root, "Backup");
        UpdaterPayloadContractValidatorTests.CreateValidPayload(app);
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(app, "PrintableBook.exe"), "old-main");
        Assert.ThrowsAny<Exception>(() => new UpdaterBackupService(new UpdaterPayloadContractValidator()).RestoreBackup(backup, app));
        Assert.Equal("old-main", File.ReadAllText(Path.Combine(app, "PrintableBook.exe")));
    }

    [Fact]
    public void CreateBackupKeepsExistingFinalBackupWhenNewSourceIsInvalid()
    {
        var app = Path.Combine(root, "App");
        var backup = Path.Combine(root, "Updates", "backup", "0.2.0");
        UpdaterPayloadContractValidatorTests.CreateValidPayload(app);
        UpdaterPayloadContractValidatorTests.CreateValidPayload(backup);
        File.WriteAllText(Path.Combine(backup, "PrintableBook.exe"), "previous-backup");
        File.Delete(Path.Combine(app, "PrintableBook.Updater.exe"));

        Assert.Throws<InvalidDataException>(() => new UpdaterBackupService(new UpdaterPayloadContractValidator()).CreateBackup(app, backup));
        Assert.Equal("previous-backup", File.ReadAllText(Path.Combine(backup, "PrintableBook.exe")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
