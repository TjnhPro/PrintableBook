namespace PrintableBook.Core.Application.Updates;

public sealed record UpdateAssetInfo(
    string Name,
    Uri DownloadUri,
    long SizeBytes);
