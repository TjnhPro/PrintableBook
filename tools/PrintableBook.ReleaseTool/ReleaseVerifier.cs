using System.Security.Cryptography;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool;

internal sealed class ReleaseVerifier
{
    private const long MaxManifestBytes = 16 * 1024;

    public void Verify(ReleaseArtifactPaths paths, Version version, string runtimeIdentifier, ReadOnlySpan<byte> publicKey)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var names = UpdateReleaseNames.For(version, runtimeIdentifier);
        RequireFile(paths.ArchivePath, names.Archive);
        RequireFile(paths.ChecksumPath, names.Checksum);
        RequireFile(paths.ManifestPath, names.Manifest);
        RequireFile(paths.SignaturePath, names.Signature);
        if (publicKey.Length != Ed25519UpdateSignature.PublicKeySize) throw new ArgumentException("Public key must be 32 bytes.", nameof(publicKey));
        if (new FileInfo(paths.ManifestPath).Length > MaxManifestBytes) throw new InvalidDataException("Release manifest exceeds the maximum size.");

        var manifestBytes = File.ReadAllBytes(paths.ManifestPath);
        var signature = UpdateSignatureFileCodec.Decode(File.ReadAllBytes(paths.SignaturePath));
        if (!Ed25519UpdateSignature.Verify(manifestBytes, signature, publicKey)) throw new InvalidDataException("Release manifest signature is invalid.");
        var manifest = UpdateManifestCodec.Deserialize(manifestBytes);
        if (manifest.Version != version.ToString(3) || manifest.RuntimeIdentifier != runtimeIdentifier || manifest.Archive.Name != names.Archive || manifest.Checksum.Name != names.Checksum)
        {
            throw new InvalidDataException("Release manifest does not match the requested artifact names.");
        }

        VerifyFile(paths.ArchivePath, manifest.Archive);
        VerifyFile(paths.ChecksumPath, manifest.Checksum);
        var checksumArchiveHash = ReleaseChecksumFile.Parse(File.ReadAllBytes(paths.ChecksumPath), names.Archive);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(checksumArchiveHash), Convert.FromHexString(manifest.Archive.Sha256))) throw new InvalidDataException("Release checksum does not match the signed archive hash.");
        new ReleasePackageContractVerifier().Verify(paths.ArchivePath, version, runtimeIdentifier);
    }

    private static void RequireFile(string path, string expectedName)
    {
        if (Path.GetFileName(path) != expectedName || !File.Exists(path)) throw new InvalidDataException("A required release artifact is missing.");
    }

    private static void VerifyFile(string path, UpdateReleaseAsset expected)
    {
        var actual = ReleaseSigner.HashFile(path);
        if (actual.Size != expected.SizeBytes || !CryptographicOperations.FixedTimeEquals(actual.Hash, Convert.FromHexString(expected.Sha256)))
        {
            throw new InvalidDataException("Release artifact does not match its signed manifest entry.");
        }
    }
}
