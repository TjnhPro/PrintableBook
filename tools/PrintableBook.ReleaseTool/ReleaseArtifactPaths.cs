using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool;

internal sealed record ReleaseArtifactPaths(string ArchivePath, string ChecksumPath, string ManifestPath, string SignaturePath)
{
    public static ReleaseArtifactPaths Create(string releaseRoot, Version version, string runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(releaseRoot)) throw new ArgumentException("A release root is required.", nameof(releaseRoot));
        var names = UpdateReleaseNames.For(version, runtimeIdentifier);
        var root = Path.GetFullPath(releaseRoot);
        return new ReleaseArtifactPaths(
            Path.Combine(root, names.Archive),
            Path.Combine(root, names.Checksum),
            Path.Combine(root, names.Manifest),
            Path.Combine(root, names.Signature));
    }
}
