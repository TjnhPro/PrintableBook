using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class UpdaterPayloadContractValidatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly UpdaterPayloadContractValidator validator = new();

    [Fact]
    public void StagedPayloadRequiresExactControlledRoot()
    {
        CreateValidPayload(root);
        validator.ValidateStagedPayload(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), "user");
        Assert.Throws<InvalidDataException>(() => validator.ValidateStagedPayload(root));
    }

    [Theory]
    [InlineData("PrintableBook.exe")]
    [InlineData("PrintableBook.Updater.exe")]
    [InlineData("Frontend/index.html")]
    [InlineData("Frontend/js/app.js")]
    [InlineData("Frontend/assets/printable-book-logo.png")]
    [InlineData("Metadata/cover_key.txt")]
    [InlineData("Metadata/interior_key.txt")]
    [InlineData(".playwright/package/package.json")]
    [InlineData(".playwright/node/win32_x64/node.exe")]
    public void BothContractsRejectMissingRequiredFiles(string relativePath)
    {
        CreateValidPayload(root);
        File.Delete(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Throws<InvalidDataException>(() => validator.ValidateStagedPayload(root));
        Assert.Throws<InvalidDataException>(() => validator.ValidateInstalledPayload(root));
    }

    [Theory]
    [InlineData("Frontend/css")]
    [InlineData("Frontend/js")]
    [InlineData("Frontend/assets")]
    [InlineData("Metadata")]
    [InlineData(".playwright")]
    [InlineData(".playwright/package")]
    [InlineData(".playwright/node")]
    [InlineData(".playwright/node/win32_x64")]
    public void BothContractsRejectMissingRequiredDirectories(string relativePath)
    {
        CreateValidPayload(root);
        Directory.Delete(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)), true);
        Assert.Throws<InvalidDataException>(() => validator.ValidateStagedPayload(root));
        Assert.Throws<InvalidDataException>(() => validator.ValidateInstalledPayload(root));
    }

    [Fact]
    public void InstalledPayloadPermitsUserData()
    {
        CreateValidPayload(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), "user");
        Directory.CreateDirectory(Path.Combine(root, "brands", "brand-a"));
        File.WriteAllText(Path.Combine(root, "brands", "brand-a", "data.txt"), "user");
        validator.ValidateInstalledPayload(root);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    internal static void CreateValidPayload(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "Frontend", "css"));
        Directory.CreateDirectory(Path.Combine(path, "Frontend", "js"));
        Directory.CreateDirectory(Path.Combine(path, "Frontend", "assets"));
        Directory.CreateDirectory(Path.Combine(path, "Metadata"));
        Directory.CreateDirectory(Path.Combine(path, ".playwright", "package"));
        Directory.CreateDirectory(Path.Combine(path, ".playwright", "node", "win32_x64"));
        foreach (var file in new[] { "PrintableBook.exe", "PrintableBook.Updater.exe", "Frontend/index.html", "Frontend/js/app.js", "Frontend/css/app.css", "Frontend/assets/printable-book-logo.png", "Metadata/cover_key.txt", "Metadata/interior_key.txt", ".playwright/package/package.json", ".playwright/node/win32_x64/node.exe" })
            File.WriteAllText(Path.Combine(path, file.Replace('/', Path.DirectorySeparatorChar)), file);
    }
}
