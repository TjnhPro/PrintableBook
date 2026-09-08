namespace PrintableBook.Core.Application.Updates;

public static class VersionComparer
{
    public static bool IsNewer(Version candidate, Version current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);

        return Normalize(candidate).CompareTo(Normalize(current)) > 0;
    }

    private static Version Normalize(Version version)
    {
        var patch = version.Build < 0 ? 0 : version.Build;
        return new Version(version.Major, version.Minor, patch);
    }
}
