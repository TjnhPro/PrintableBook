namespace PrintableBook.Core.Application.Updates;

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    UpdateInfo? LatestRelease,
    UpdateAvailability Availability);
