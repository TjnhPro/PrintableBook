using System.IO.Compression;
using PrintableBook.ReleaseTool;

namespace PrintableBook.ReleaseTool.Tests;

public sealed class ReleasePackageContractVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PrintableBook.Package.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptsTheFlatAndExactWrappedPackageForms(bool wrapped)
    {
        var archive = CreateArchive(wrapped, ValidFiles);

        new ReleasePackageContractVerifier().Verify(archive, new Version(0, 2, 0), "win-x64");
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("brands/logo.png")]
    [InlineData("Output/file.txt")]
    [InlineData("../evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("C:/evil.txt")]
    public void RejectsUnexpectedOrUnsafePackageEntries(string extraFile)
    {
        var archive = CreateArchive(false, ValidFiles.Append(extraFile));

        Assert.Throws<InvalidDataException>(() => new ReleasePackageContractVerifier().Verify(archive, new Version(0, 2, 0), "win-x64"));
    }

    [Fact]
    public void RejectsMissingRequiredFilesAndMultipleWrapperRoots()
    {
        var missing = CreateArchive(false, ValidFiles.Where(file => file != "PrintableBook.Updater.exe"));
        var multiple = CreateArchive(true, ValidFiles.Append("other-root/file.txt"));

        Assert.Throws<InvalidDataException>(() => new ReleasePackageContractVerifier().Verify(missing, new Version(0, 2, 0), "win-x64"));
        Assert.Throws<InvalidDataException>(() => new ReleasePackageContractVerifier().Verify(multiple, new Version(0, 2, 0), "win-x64"));
    }

    [Fact]
    public void RejectsEmptyUnexpectedRootDirectories()
    {
        foreach (var directory in new[] { "brands/", "evil/" })
        {
            var archive = CreateArchive(false, ValidFiles);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
            {
                zip.CreateEntry(directory);
            }

            Assert.Throws<InvalidDataException>(() => new ReleasePackageContractVerifier().Verify(archive, new Version(0, 2, 0), "win-x64"));
        }
    }

    private string CreateArchive(bool wrapped, IEnumerable<string> files)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(wrapped ? $"PrintableBook-0.2.0-win-x64/{file}" : file);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("fixture");
        }

        return path;
    }

    private static readonly string[] ValidFiles = ["PrintableBook.exe", "PrintableBook.Updater.exe", "Frontend/index.html", "Frontend/js/app.js", "Frontend/assets/printable-book-logo.png"];

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
