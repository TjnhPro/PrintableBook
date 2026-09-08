namespace PrintableBook.UpdateSecurity;

public static class UpdateManifestContract
{
    public const int CurrentSchemaVersion = 1;
    public const string ProductName = "PrintableBook";
    public const string RuntimeIdentifier = "win-x64";

    public static void Validate(UpdateReleaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != CurrentSchemaVersion || manifest.Product != ProductName || manifest.RuntimeIdentifier != RuntimeIdentifier || !TryParseThreePartVersion(manifest.Version, out var version)) throw new InvalidDataException("Update manifest contract is invalid.");
        var names = UpdateReleaseNames.For(version, manifest.RuntimeIdentifier);
        ValidateAsset(manifest.Archive, names.Archive);
        ValidateAsset(manifest.Checksum, names.Checksum);
    }

    private static void ValidateAsset(UpdateReleaseAsset? asset, string expectedName)
    {
        if (asset is null || asset.Name != expectedName || asset.SizeBytes <= 0 || !IsLowercaseSha256(asset.Sha256)) throw new InvalidDataException("Update manifest asset is invalid.");
    }

    private static bool TryParseThreePartVersion(string? value, out Version version)
    {
        version = default!;
        if (value?.Split('.') is not [var major, var minor, var build] || !int.TryParse(major, out _) || !int.TryParse(minor, out _) || !int.TryParse(build, out _) || !Version.TryParse(value, out var parsed) || parsed is null || parsed.Build < 0 || parsed.Revision >= 0) return false;
        version = parsed;
        return true;
    }

    private static bool IsLowercaseSha256(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
