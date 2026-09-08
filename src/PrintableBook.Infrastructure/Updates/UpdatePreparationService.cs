using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Infrastructure.Updates;

public sealed class UpdatePreparationService(
    UpdateStorageLayout storageLayout,
    HttpUpdateAssetDownloader downloader,
    Sha256PackageVerifier verifier,
    ZipUpdatePackageExtractor extractor,
    UpdatePackageContractValidator validator)
    : IUpdatePreparationService
{
    private readonly SemaphoreSlim preparationGate = new(1, 1);

    public async ValueTask<PreparedUpdate> PrepareAsync(
        UpdateInfo update,
        IProgress<UpdatePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await preparationGate.WaitAsync(cancellationToken);
        try
        {
            return await PrepareCoreAsync(update, progress, cancellationToken);
        }
        finally
        {
            preparationGate.Release();
        }
    }

    private async ValueTask<PreparedUpdate> PrepareCoreAsync(
        UpdateInfo update,
        IProgress<UpdatePreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloadDirectory = storageLayout.GetDownloadDirectory(update.Version);
        var stagingVersionDirectory = storageLayout.GetStagingVersionDirectory(update.Version);
        var readyPayloadDirectory = storageLayout.GetReadyPayloadDirectory(update.Version);
        var extractionDirectory = storageLayout.CreateTemporaryExtractionDirectory(update.Version);

        try
        {
            DeleteDirectoryIfExists(downloadDirectory);
            DeleteDirectoryIfExists(stagingVersionDirectory);
            Directory.CreateDirectory(downloadDirectory);
            Directory.CreateDirectory(stagingVersionDirectory);

            var archivePath = Path.Combine(downloadDirectory, update.Package.Archive.Name);
            progress?.Report(new UpdatePreparationProgress(
                UpdatePreparationStage.DownloadingArchive,
                0,
                update.Package.Archive.SizeBytes));
            await downloader.DownloadAsync(
                update.Package.Archive,
                archivePath,
                new Progress<long>(received => progress?.Report(new UpdatePreparationProgress(
                    UpdatePreparationStage.DownloadingArchive,
                    received,
                    update.Package.Archive.SizeBytes))),
                cancellationToken);

            var checksumPath = Path.Combine(downloadDirectory, update.Package.Checksum.Name);
            progress?.Report(new UpdatePreparationProgress(
                UpdatePreparationStage.DownloadingChecksum,
                0,
                update.Package.Checksum.SizeBytes));
            await downloader.DownloadAsync(
                update.Package.Checksum,
                checksumPath,
                new Progress<long>(received => progress?.Report(new UpdatePreparationProgress(
                    UpdatePreparationStage.DownloadingChecksum,
                    received,
                    update.Package.Checksum.SizeBytes))),
                cancellationToken);

            progress?.Report(new UpdatePreparationProgress(UpdatePreparationStage.Verifying));
            await verifier.VerifyAsync(
                archivePath,
                checksumPath,
                update.Package.Archive.Name,
                cancellationToken);

            progress?.Report(new UpdatePreparationProgress(UpdatePreparationStage.Extracting));
            var extractedPayloadRoot = await extractor.ExtractAsync(
                archivePath,
                extractionDirectory,
                cancellationToken);

            progress?.Report(new UpdatePreparationProgress(UpdatePreparationStage.Validating));
            validator.Validate(extractedPayloadRoot);

            Directory.Move(extractedPayloadRoot, readyPayloadDirectory);
            if (Directory.Exists(extractionDirectory))
            {
                Directory.Delete(extractionDirectory, recursive: true);
            }

            progress?.Report(new UpdatePreparationProgress(UpdatePreparationStage.Ready));
            return new PreparedUpdate(update.Version, readyPayloadDirectory);
        }
        catch
        {
            DeleteDirectoryIfExists(downloadDirectory);
            DeleteDirectoryIfExists(stagingVersionDirectory);
            throw;
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
