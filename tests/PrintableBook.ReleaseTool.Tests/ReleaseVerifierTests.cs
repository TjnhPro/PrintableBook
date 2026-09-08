using PrintableBook.ReleaseTool;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool.Tests;

public sealed class ReleaseVerifierTests : IDisposable
{
    private static readonly byte[] TestSeed = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PrintableBook.ReleaseVerifier.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("archive")]
    [InlineData("checksum")]
    [InlineData("manifest")]
    [InlineData("signature")]
    public void RejectsAnyTamperedSignedReleaseArtifact(string target)
    {
        var paths = CreateSignedFixture();
        var path = target switch { "archive" => paths.ArchivePath, "checksum" => paths.ChecksumPath, "manifest" => paths.ManifestPath, _ => paths.SignaturePath };
        File.AppendAllText(path, "x");

        Assert.Throws<InvalidDataException>(() => Verify(paths));
    }

    [Fact]
    public void RejectsWrongVersionAndRuntime()
    {
        var paths = CreateSignedFixture();

        Assert.Throws<InvalidDataException>(() => new ReleaseVerifier().Verify(paths, new Version(0, 2, 1), "win-x64", Ed25519UpdateSignature.DerivePublicKey(TestSeed)));
        Assert.Throws<ArgumentException>(() => new ReleaseVerifier().Verify(paths, new Version(0, 2, 0), "linux-x64", Ed25519UpdateSignature.DerivePublicKey(TestSeed)));
    }

    [Fact]
    public void RejectsSignedArchiveWithAnUnexpectedEmptyRootDirectory()
    {
        var paths = CreateSignedFixture("brands/");

        Assert.Throws<InvalidDataException>(() => Verify(paths));
    }

    private ReleaseArtifactPaths CreateSignedFixture(string? extraEntry = null)
    {
        Directory.CreateDirectory(_root);
        var paths = ReleaseArtifactPaths.Create(_root, new Version(0, 2, 0), "win-x64");
        using (var archive = System.IO.Compression.ZipFile.Open(paths.ArchivePath, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var file in new[] { "PrintableBook.exe", "PrintableBook.Updater.exe", "Frontend/index.html", "Frontend/js/app.js", "Frontend/assets/printable-book-logo.png" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(file).Open());
                writer.Write("fixture");
            }
            if (extraEntry is not null) archive.CreateEntry(extraEntry);
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(paths.ArchivePath))).ToLowerInvariant();
        File.WriteAllText(paths.ChecksumPath, $"{hash}  {Path.GetFileName(paths.ArchivePath)}\n");
        new ReleaseSigner().Sign(paths, new Version(0, 2, 0), "win-x64", TestSeed, Ed25519UpdateSignature.DerivePublicKey(TestSeed));
        return paths;
    }

    private static void Verify(ReleaseArtifactPaths paths) => new ReleaseVerifier().Verify(paths, new Version(0, 2, 0), "win-x64", Ed25519UpdateSignature.DerivePublicKey(TestSeed));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
