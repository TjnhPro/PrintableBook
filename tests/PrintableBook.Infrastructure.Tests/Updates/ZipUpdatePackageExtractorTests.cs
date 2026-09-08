using System.IO.Compression;
using System.Text;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class ZipUpdatePackageExtractorTests
{
    [Fact]
    public async Task ExtractAsyncAcceptsFlatPayloadLayout()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "package.zip");
        CreateZip(archivePath, PayloadEntries());
        var extraction = Path.Combine(directory.Path, "extraction");

        var root = await new ZipUpdatePackageExtractor().ExtractAsync(archivePath, extraction);

        Assert.Equal(extraction, root);
        Assert.True(File.Exists(Path.Combine(root, "PrintableBook.exe")));
    }

    [Fact]
    public async Task ExtractAsyncAcceptsSingleWrappedPayloadLayout()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "package.zip");
        CreateZip(archivePath, PayloadEntries("PrintableBook-0.1.2-win-x64/"));
        var extraction = Path.Combine(directory.Path, "extraction");

        var root = await new ZipUpdatePackageExtractor().ExtractAsync(archivePath, extraction);

        Assert.Equal(Path.Combine(extraction, "PrintableBook-0.1.2-win-x64"), root);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("Frontend/../../outside.txt")]
    public async Task ExtractAsyncRejectsPathTraversal(string entryPath)
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "package.zip");
        CreateZip(archivePath, (entryPath, Bytes("outside")));
        var extraction = Path.Combine(directory.Path, "extraction");

        await Assert.ThrowsAsync<InvalidDataException>(async () => await new ZipUpdatePackageExtractor().ExtractAsync(archivePath, extraction).AsTask());

        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.txt")));
    }

    [Fact]
    public async Task ExtractAsyncRejectsAmbiguousPayloadRoots()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "package.zip");
        CreateZip(
            archivePath,
            ("PackageA/PrintableBook.exe", Bytes("exe")),
            ("PackageB/PrintableBook.exe", Bytes("exe")));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await new ZipUpdatePackageExtractor().ExtractAsync(archivePath, Path.Combine(directory.Path, "extraction")).AsTask());
    }

    [Fact]
    public async Task ExtractAsyncClassifiesMalformedZipAsInvalidData()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "package.zip");
        await File.WriteAllBytesAsync(archivePath, Bytes("not a zip"));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await new ZipUpdatePackageExtractor().ExtractAsync(archivePath, Path.Combine(directory.Path, "extraction")).AsTask());
    }

    private static (string Path, byte[] Content)[] PayloadEntries(string prefix = "") =>
    [
        ($"{prefix}PrintableBook.exe", Bytes("exe")),
        ($"{prefix}Frontend/index.html", Bytes("html")),
        ($"{prefix}Frontend/js/app.js", Bytes("js")),
        ($"{prefix}Frontend/assets/printable-book-logo.png", Bytes("logo")),
        ($"{prefix}Frontend/css/app.css", Bytes("css")),
    ];

    private static void CreateZip(string archivePath, params (string Path, byte[] Content)[] entries)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var entryData in entries)
        {
            var entry = archive.CreateEntry(entryData.Path);
            using var entryStream = entry.Open();
            entryStream.Write(entryData.Content);
        }
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

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
