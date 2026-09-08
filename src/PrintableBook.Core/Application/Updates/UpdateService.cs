namespace PrintableBook.Core.Application.Updates;

public sealed class UpdateService(
    IUpdateFeed updateFeed,
    IApplicationVersionProvider versionProvider) : IUpdateService
{
    public async ValueTask<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateFeed);
        ArgumentNullException.ThrowIfNull(versionProvider);

        var currentVersion = versionProvider.CurrentVersion;
        var latestRelease = await updateFeed.GetLatestStableAsync(cancellationToken);

        var availability =
            latestRelease is not null &&
            VersionComparer.IsNewer(latestRelease.Version, currentVersion)
                ? UpdateAvailability.Available
                : UpdateAvailability.UpToDate;

        return new UpdateCheckResult(currentVersion, latestRelease, availability);
    }
}
