using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Desktop.Updates;

public sealed record DesktopUpdateSnapshot(
    DesktopUpdatePhase Phase,
    Version CurrentVersion,
    UpdateInfo? LatestRelease,
    UpdatePreparationStage? PreparationStage,
    long BytesReceived,
    long? TotalBytes,
    DateTimeOffset? LastCheckedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    bool CanCheck,
    bool CanDownload,
    bool CanInstall);
