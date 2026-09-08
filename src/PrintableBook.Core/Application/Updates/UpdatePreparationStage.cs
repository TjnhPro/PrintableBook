namespace PrintableBook.Core.Application.Updates;

public enum UpdatePreparationStage
{
    DownloadingArchive,
    DownloadingChecksum,
    Verifying,
    Extracting,
    Validating,
    Ready
}
