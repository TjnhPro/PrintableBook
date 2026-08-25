using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public sealed record CacheCleanupRequest;

public sealed class CacheCleanupWorker(
    IApplicationRootDiscovery discovery,
    IBookWorkspaceStateStore stateStore,
    IFileSystem fileSystem,
    IBookStorageMaintenance storageMaintenance)
    : BackgroundTaskWorker<CacheCleanupRequest, CacheCleanupResult>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.CacheCleanup;

    protected override async ValueTask<CacheCleanupResult> ExecuteTypedAsync(
        CacheCleanupRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        context.Report("Scanning");
        var discovered = await discovery.DiscoverAsync(cancellationToken);
        var total = discovered.Books.Count;
        var results = new List<CacheCleanupBookResult>(total);
        var cleaned = 0;
        var skipped = 0;
        var failed = 0;
        long freedBytes = 0;

        for (var index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var book = discovered.Books[index];
            context.Report("Cleaning", index, total, subject: book.Id.Value);
            try
            {
                var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
                if (state.Status != BookProcessingStatus.Completed)
                {
                    skipped++;
                    results.Add(new CacheCleanupBookResult(book.Id, "Skipped", 0, $"Workspace status is {state.Status}."));
                    context.Report("Cleaning", index + 1, total, $"Skipped: {state.Status}", book.Id.Value);
                    continue;
                }

                var artifacts = state.PublishedArtifactReferences ?? [];
                if (artifacts.Count == 0)
                {
                    skipped++;
                    results.Add(new CacheCleanupBookResult(book.Id, "Skipped", 0, "No published output is recorded."));
                    context.Report("Cleaning", index + 1, total, "Skipped: no published output", book.Id.Value);
                    continue;
                }

                var outputMissing = false;
                foreach (var artifact in artifacts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await fileSystem.FileExistsAsync(new FileReference(artifact), cancellationToken))
                    {
                        outputMissing = true;
                        break;
                    }
                }

                if (outputMissing)
                {
                    skipped++;
                    results.Add(new CacheCleanupBookResult(book.Id, "Skipped", 0, "Published output is missing."));
                    context.Report("Cleaning", index + 1, total, "Skipped: output missing", book.Id.Value);
                    continue;
                }

                var released = await storageMaintenance.ClearHeavyProcessingCacheAsync(book.Workspace, cancellationToken);
                cleaned++;
                freedBytes += released;
                results.Add(new CacheCleanupBookResult(book.Id, "Cleaned", released, null));
                context.Report("Cleaning", index + 1, total, $"Freed {released} bytes", book.Id.Value);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (BookStorageCleanupException exception)
            {
                failed++;
                freedBytes += exception.FreedBytes;
                results.Add(new CacheCleanupBookResult(book.Id, "Failed", exception.FreedBytes, "Cache cleanup failed."));
                context.Report("Cleaning", index + 1, total, "Failed", book.Id.Value);
            }
            catch
            {
                failed++;
                results.Add(new CacheCleanupBookResult(book.Id, "Failed", 0, "Cache cleanup failed."));
                context.Report("Cleaning", index + 1, total, "Failed", book.Id.Value);
            }
        }

        context.Report("Completed", total, total, $"Freed {freedBytes} bytes");
        return new CacheCleanupResult(total, cleaned, skipped, failed, freedBytes, results);
    }
}
