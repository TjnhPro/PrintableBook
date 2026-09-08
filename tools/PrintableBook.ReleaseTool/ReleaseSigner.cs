using System.Security.Cryptography;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool;

internal sealed class ReleaseSigner
{
    public void Sign(ReleaseArtifactPaths paths, Version version, string runtimeIdentifier, ReadOnlySpan<byte> privateSeed, ReadOnlySpan<byte> expectedPublicKey)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var names = UpdateReleaseNames.For(version, runtimeIdentifier);
        RequireFile(paths.ArchivePath, names.Archive);
        RequireFile(paths.ChecksumPath, names.Checksum);
        if (expectedPublicKey.Length != Ed25519UpdateSignature.PublicKeySize) throw new ArgumentException("Expected public key must be 32 bytes.", nameof(expectedPublicKey));
        var archiveHash = HashFile(paths.ArchivePath);
        var checksumBytes = File.ReadAllBytes(paths.ChecksumPath);
        var checksumHash = UpdateChecksumFileCodec.Parse(checksumBytes, names.Archive);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(checksumHash), archiveHash.Hash)) throw new InvalidDataException("Checksum file does not match the release archive.");

        var derivedPublicKey = Ed25519UpdateSignature.DerivePublicKey(privateSeed);
        if (!CryptographicOperations.FixedTimeEquals(derivedPublicKey, expectedPublicKey)) throw new InvalidDataException("Signing key does not match the pinned production public key.");
        var checksumFileHash = HashFile(paths.ChecksumPath);
        var manifest = new UpdateReleaseManifest(
            UpdateManifestContract.CurrentSchemaVersion,
            UpdateManifestContract.ProductName,
            version.ToString(3),
            runtimeIdentifier,
            new UpdateReleaseAsset(names.Archive, archiveHash.Size, Convert.ToHexString(archiveHash.Hash).ToLowerInvariant()),
            new UpdateReleaseAsset(names.Checksum, checksumFileHash.Size, Convert.ToHexString(checksumFileHash.Hash).ToLowerInvariant()));
        var manifestBytes = UpdateManifestCodec.Serialize(manifest);
        var signature = Ed25519UpdateSignature.Sign(manifestBytes, privateSeed);
        File.WriteAllBytes(paths.ManifestPath, manifestBytes);
        File.WriteAllBytes(paths.SignaturePath, UpdateSignatureFileCodec.Encode(signature));
    }

    private static void RequireFile(string path, string expectedName)
    {
        if (Path.GetFileName(path) != expectedName || !File.Exists(path)) throw new InvalidDataException("A required release artifact is missing.");
    }

    internal static (long Size, byte[] Hash) HashFile(string path) => (new FileInfo(path).Length, SHA256.HashData(File.ReadAllBytes(path)));
}
