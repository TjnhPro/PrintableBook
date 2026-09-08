using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class UpdaterPayloadInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void InstallReplacesControlledPayloadAndPreservesUserData()
    {
        var app = Path.Combine(root, "App"); var payload = Path.Combine(root, "Payload");
        UpdaterPayloadContractValidatorTests.CreateValidPayload(app);
        UpdaterPayloadContractValidatorTests.CreateValidPayload(payload);
        File.WriteAllText(Path.Combine(app, "PrintableBook.exe"), "old");
        File.WriteAllText(Path.Combine(app, "Frontend", "obsolete.js"), "obsolete");
        File.WriteAllText(Path.Combine(app, "settings.json"), "user");
        File.WriteAllText(Path.Combine(payload, "PrintableBook.exe"), "new");
        new UpdaterPayloadInstaller(new UpdaterPayloadContractValidator()).Install(payload, app);
        Assert.Equal("new", File.ReadAllText(Path.Combine(app, "PrintableBook.exe")));
        Assert.False(File.Exists(Path.Combine(app, "Frontend", "obsolete.js")));
        Assert.Equal("user", File.ReadAllText(Path.Combine(app, "settings.json")));
    }

    [Fact]
    public void InvalidStagedPayloadDoesNotMutateApp()
    {
        var app = Path.Combine(root, "App"); var payload = Path.Combine(root, "Payload");
        UpdaterPayloadContractValidatorTests.CreateValidPayload(app);
        UpdaterPayloadContractValidatorTests.CreateValidPayload(payload);
        File.WriteAllText(Path.Combine(app, "PrintableBook.exe"), "old");
        File.Delete(Path.Combine(payload, "PrintableBook.Updater.exe"));
        Assert.Throws<InvalidDataException>(() => new UpdaterPayloadInstaller(new UpdaterPayloadContractValidator()).Install(payload, app));
        Assert.Equal("old", File.ReadAllText(Path.Combine(app, "PrintableBook.exe")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
