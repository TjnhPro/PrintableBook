using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PrintableBook.ReleaseTool;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool.Tests;

public sealed class ReleaseSignerTests : IDisposable
{
    private static readonly byte[] TestSeed = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PrintableBook.ReleaseSigner.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SignsTheExactManifestAndSignatureFiles()
    {
        var paths = CreateValidFixture();
        var publicKey = Ed25519UpdateSignature.DerivePublicKey(TestSeed);

        new ReleaseSigner().Sign(paths, new Version(0, 2, 0), "win-x64", TestSeed, publicKey);

        Assert.True(File.Exists(paths.ManifestPath));
        Assert.True(File.Exists(paths.SignaturePath));
        var manifestBytes = File.ReadAllBytes(paths.ManifestPath);
        var manifest = UpdateManifestCodec.Deserialize(manifestBytes);
        Assert.Equal(new FileInfo(paths.ArchivePath).Length, manifest.Archive.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths.ArchivePath))).ToLowerInvariant(), manifest.Archive.Sha256);
        Assert.Equal(new FileInfo(paths.ChecksumPath).Length, manifest.Checksum.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths.ChecksumPath))).ToLowerInvariant(), manifest.Checksum.Sha256);
        Assert.True(Ed25519UpdateSignature.Verify(manifestBytes, UpdateSignatureFileCodec.Decode(File.ReadAllBytes(paths.SignaturePath)), publicKey));
    }

    [Fact]
    public void RejectsMissingOrMismatchedSigningInputs()
    {
        var paths = CreateValidFixture();
        var key = Ed25519UpdateSignature.DerivePublicKey(TestSeed);
        File.WriteAllText(paths.ChecksumPath, "bad\n", Encoding.ASCII);
        var signer = new ReleaseSigner();

        Assert.Throws<InvalidDataException>(() => signer.Sign(paths, new Version(0, 2, 0), "win-x64", TestSeed, key));
        WriteChecksum(paths);
        Assert.Throws<ArgumentException>(() => signer.Sign(paths, new Version(0, 2, 0), "win-x64", new byte[31], key));
        Assert.Throws<ArgumentException>(() => signer.Sign(paths, new Version(0, 2, 0), "win-x64", TestSeed, new byte[31]));
        Assert.Throws<InvalidDataException>(() => signer.Sign(paths, new Version(0, 2, 0), "win-x64", TestSeed, new byte[32]));
    }

    internal ReleaseArtifactPaths CreateValidFixture()
    {
        Directory.CreateDirectory(_root);
        var paths = ReleaseArtifactPaths.Create(_root, new Version(0, 2, 0), "win-x64");
        using (var archive = ZipFile.Open(paths.ArchivePath, ZipArchiveMode.Create))
        {
            foreach (var file in new[] { "PrintableBook.exe", "PrintableBook.Updater.exe", "Frontend/index.html", "Frontend/js/app.js", "Frontend/assets/printable-book-logo.png" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(file).Open());
                writer.Write("fixture");
            }
        }

        WriteChecksum(paths);
        return paths;
    }

    private static void WriteChecksum(ReleaseArtifactPaths paths) => File.WriteAllText(paths.ChecksumPath, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths.ArchivePath))).ToLowerInvariant() + "  " + Path.GetFileName(paths.ArchivePath) + "\n", Encoding.ASCII);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
