namespace PrintableBook.Core.Application.Updates;

public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string Name,
    string? ReleaseNotes,
    DateTimeOffset PublishedAtUtc,
    Uri ReleasePageUri,
    UpdatePackageInfo Package);
