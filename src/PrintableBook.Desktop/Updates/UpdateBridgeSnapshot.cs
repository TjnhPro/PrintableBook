using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Desktop.Updates;

internal sealed record UpdateBridgeSnapshot(
    string Phase,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseName,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAtUtc,
    string? ReleasePageUrl,
    string? PreparationStage,
    long BytesReceived,
    long? TotalBytes,
    DateTimeOffset? LastCheckedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    bool CanCheck,
    bool CanDownload,
    bool CanInstall)
{
    public static UpdateBridgeSnapshot From(DesktopUpdateSnapshot snapshot) => new(
        snapshot.Phase.ToString(), snapshot.CurrentVersion.ToString(3), snapshot.LatestRelease?.Version.ToString(3), snapshot.LatestRelease?.Name,
        snapshot.LatestRelease?.ReleaseNotes, snapshot.LatestRelease?.PublishedAtUtc, snapshot.LatestRelease?.ReleasePageUri.ToString(), snapshot.PreparationStage?.ToString(),
        snapshot.BytesReceived, snapshot.TotalBytes, snapshot.LastCheckedAtUtc, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.CanCheck, snapshot.CanDownload, snapshot.CanInstall);
}
