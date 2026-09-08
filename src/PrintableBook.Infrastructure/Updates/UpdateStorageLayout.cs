namespace PrintableBook.Infrastructure.Updates;

public sealed class UpdateStorageLayout(IUpdateStorageRootProvider rootProvider)
{
    public string GetDownloadDirectory(Version version)
    {
        return Path.Combine(rootProvider.RootPath, "downloads", VersionDirectoryName(version));
    }

    public string GetStagingVersionDirectory(Version version)
    {
        return Path.Combine(rootProvider.RootPath, "staging", VersionDirectoryName(version));
    }

    public string GetReadyPayloadDirectory(Version version)
    {
        return Path.Combine(GetStagingVersionDirectory(version), "payload");
    }

    public string CreateTemporaryExtractionDirectory(Version version)
    {
        return Path.Combine(
            GetStagingVersionDirectory(version),
            $".extracting-{Guid.NewGuid():N}");
    }

    private static string VersionDirectoryName(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Build < 0)
        {
            throw new ArgumentException(
                "Update version must include major, minor, and patch.",
                nameof(version));
        }

        return version.ToString(3);
    }
}
