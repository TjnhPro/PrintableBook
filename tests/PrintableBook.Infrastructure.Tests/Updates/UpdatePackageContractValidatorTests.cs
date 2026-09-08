using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class UpdatePackageContractValidatorTests
{
    [Fact]
    public void ValidateAcceptsPortablePackageContract()
    {
        using var directory = new TemporaryDirectory();
        CreateValidPayload(directory.Path);

        new UpdatePackageContractValidator().Validate(directory.Path);
    }

    [Theory]
    [InlineData("PrintableBook.exe")]
    [InlineData("PrintableBook.Updater.exe")]
    [InlineData("Frontend")]
    [InlineData("Frontend/index.html")]
    [InlineData("Frontend/js")]
    [InlineData("Frontend/js/app.js")]
    [InlineData("Frontend/css")]
    [InlineData("Frontend/assets")]
    [InlineData("Frontend/assets/printable-book-logo.png")]
    public void ValidateRejectsMissingRequiredPath(string relativePath)
    {
        using var directory = new TemporaryDirectory();
        CreateValidPayload(directory.Path);
        var target = Path.Combine(directory.Path, relativePath);
        if (File.Exists(target))
        {
            File.Delete(target);
        }
        else
        {
            Directory.Delete(target, recursive: true);
        }

        var exception = Assert.Throws<InvalidDataException>(() => new UpdatePackageContractValidator().Validate(directory.Path));

        Assert.Contains(
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("brands", true)]
    [InlineData("sources", true)]
    [InlineData("settings.json", false)]
    [InlineData(".workspace", true)]
    [InlineData("Output", true)]
    [InlineData("malicious.dll", false)]
    [InlineData("Assets", true)]
    [InlineData("anything-else.txt", false)]
    public void ValidateRejectsUnexpectedPayloadRootEntry(string name, bool isDirectory)
    {
        using var directory = new TemporaryDirectory();
        CreateValidPayload(directory.Path);
        var path = Path.Combine(directory.Path, name);
        if (isDirectory)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            File.WriteAllText(path, "unexpected");
        }

        Assert.Throws<InvalidDataException>(() => new UpdatePackageContractValidator().Validate(directory.Path));
    }

    private static void CreateValidPayload(string root)
    {
        File.WriteAllText(Path.Combine(root, "PrintableBook.exe"), "exe");
        File.WriteAllText(Path.Combine(root, "PrintableBook.Updater.exe"), "updater");
        Directory.CreateDirectory(Path.Combine(root, "Frontend", "css"));
        Directory.CreateDirectory(Path.Combine(root, "Frontend", "js"));
        Directory.CreateDirectory(Path.Combine(root, "Frontend", "assets"));
        File.WriteAllText(Path.Combine(root, "Frontend", "index.html"), "html");
        File.WriteAllText(Path.Combine(root, "Frontend", "js", "app.js"), "js");
        File.WriteAllText(Path.Combine(root, "Frontend", "assets", "printable-book-logo.png"), "logo");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PrintableBook.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
