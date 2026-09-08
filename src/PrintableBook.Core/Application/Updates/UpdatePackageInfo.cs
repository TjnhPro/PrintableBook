namespace PrintableBook.Core.Application.Updates;

public sealed record UpdatePackageInfo(
    UpdateAssetInfo Archive,
    UpdateAssetInfo Checksum,
    string ArchiveSha256,
    string ChecksumSha256);
