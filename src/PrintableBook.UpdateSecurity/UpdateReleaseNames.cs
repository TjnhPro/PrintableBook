namespace PrintableBook.UpdateSecurity;

public sealed record UpdateReleaseAssetNames(string Archive, string Checksum, string Manifest, string Signature);

public static class UpdateReleaseNames
{
    public static UpdateReleaseAssetNames For(Version version, string runtimeIdentifier)
    {
        if (version is null || version.Build < 0 || version.Revision >= 0) throw new ArgumentException("Version must have exactly three components.", nameof(version));
        if (runtimeIdentifier != UpdateManifestContract.RuntimeIdentifier) throw new ArgumentException("The runtime identifier is not supported.", nameof(runtimeIdentifier));
        var prefix = $"PrintableBook-{version.ToString(3)}-{runtimeIdentifier}";
        return new UpdateReleaseAssetNames($"{prefix}.zip", $"{prefix}.zip.sha256", $"{prefix}.manifest.json", $"{prefix}.manifest.json.sig");
    }
}
