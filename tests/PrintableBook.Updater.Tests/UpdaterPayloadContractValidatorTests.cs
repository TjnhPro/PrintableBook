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
    public void BothContractsRejectMissingRequiredFiles(string relativePath)
    {
        CreateValidPayload(root);
        File.Delete(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
        foreach (var file in new[] { "PrintableBook.exe", "PrintableBook.Updater.exe", "Frontend/index.html", "Frontend/js/app.js", "Frontend/css/app.css", "Frontend/assets/printable-book-logo.png" })
            File.WriteAllText(Path.Combine(path, file.Replace('/', Path.DirectorySeparatorChar)), file);
    }
}
