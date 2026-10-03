using System.Security.Cryptography;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.S3Storage;

public sealed class S3StorageWorker(
    IApplicationSnapshotProvider snapshotProvider,
    IS3StorageOperationContextStore operationContexts,
    IS3ObjectSessionFactory objectSessions,
    IBookOutputLeaseCoordinator outputLeases,
    IS3PublicationReceiptStore receiptStore) : BackgroundTaskWorker<S3StorageTaskRequest, S3StorageBookView>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.S3Storage;

    protected override async ValueTask<S3StorageBookView> ExecuteTypedAsync(S3StorageTaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
    {
        if (!operationContexts.TryGet(request.OperationContextId, out var operation) || operation is null)
            throw new BackgroundTaskFailureException("s3_operation_context_missing", "The S3 operation context is unavailable. Start the operation again.");

        try
        {
            var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
            var book = snapshot.Discovery.Books.FirstOrDefault(item => item.Id.Value == request.BookId)
                ?? throw new BackgroundTaskFailureException("book_not_found", "Book was not found.");
            var summary = snapshot.BookSummaries.First(item => item.BookId == book.Id);
            var asin = S3StoragePolicy.NormalizeAsin(summary.Metadata?.Asin);
            if (asin.Length == 0) throw new BackgroundTaskFailureException("s3_asin_invalid", "A 10-character alphanumeric ASIN is required.");

            var startedAt = DateTimeOffset.UtcNow;
            var view = S3StorageBookView.Pending(book.Id.Value, asin, request.Action, operation.Settings) with { Outcome = S3StorageOutcome.Running, Phase = "preparing" };
            context.SetView(view);
            var stagingRoot = Path.Combine(snapshot.Discovery.Paths.Root.Value, ".printablebook", "s3-staging", request.OperationContextId.ToString("N"));
            var outputRoot = Path.Combine(book.Directory.Value, "Output");
            using var gate = new SemaphoreSlim(S3StoragePolicy.MaximumFileConcurrency, S3StoragePolicy.MaximumFileConcurrency);
            await SaveReceiptAsync(book.Directory, operation, view, startedAt, null, cancellationToken);

            try
            {
                EnsureContained(snapshot.Discovery.Paths.Root.Value, stagingRoot);
                Directory.CreateDirectory(stagingRoot);
                var lease = await outputLeases.TryAcquireAsync(book.Id.Value, cancellationToken)
                    ?? throw new BackgroundTaskFailureException("book_output_busy", "Book output is being replaced. Try again when production finishes.");
                S3StorageFileView[] staged;
                await using (lease)
                {
                    EnsureCapacity(outputRoot, stagingRoot, view.Files);
                    staged = await Task.WhenAll(view.Files.Select(row => StageAsync(row, outputRoot, stagingRoot, gate, cancellationToken)));
                }

                view = view with { Files = staged.OrderBy(row => row.Index).ToArray(), Phase = "comparing", CompletedCount = 0 };
                context.SetView(view);
                await using var session = await objectSessions.OpenAsync(operation.Settings, cancellationToken);
                var compared = new S3StorageFileView[staged.Length];
                var compareCompleted = 0;
                var sync = new object();
                await Task.WhenAll(staged.Select(async row =>
                {
                    var result = await CompareAsync(row, session, gate, cancellationToken);
                    lock (sync)
                    {
                        compared[result.Index] = result;
                        compareCompleted++;
                        view = view with { Files = MergeRows(staged, compared), CompletedCount = compareCompleted };
                        context.SetView(view);
                    }
                }));

                view = view with { Files = compared, CompletedCount = compared.Length, LastCheckedAtUtc = DateTimeOffset.UtcNow };
                context.SetView(view);
                await SaveReceiptAsync(book.Directory, operation, view, startedAt, null, cancellationToken);
                if (request.Action == S3StorageAction.Check)
                {
                    view = view with { Outcome = compared.Any(IsFailure) ? S3StorageOutcome.CompletedWithErrors : S3StorageOutcome.Completed, Phase = "completed" };
                    context.SetView(view);
                    await SaveReceiptAsync(book.Directory, operation, view, startedAt, DateTimeOffset.UtcNow, cancellationToken);
                    return view;
                }

                if (compared.Any(row => row.State is S3StorageFileState.MissingLocal or S3StorageFileState.Failed))
                {
                    view = view with { Outcome = S3StorageOutcome.CompletedWithErrors, Phase = "blocked", WarningCode = "s3_upload_preflight_failed" };
                    context.SetView(view);
                    await SaveReceiptAsync(book.Directory, operation, view, startedAt, DateTimeOffset.UtcNow, cancellationToken);
                    return view;
                }

                var uploadIndexes = compared.Where(row => row.State is S3StorageFileState.MissingRemote or S3StorageFileState.Changed or S3StorageFileState.SyncedButNotPublic).Select(row => row.Index).ToHashSet();
                view = view with { Phase = "publishing", UploadTotalCount = uploadIndexes.Count, UploadCompletedCount = 0 };
                context.SetView(view);
                var uploadedRows = compared.ToArray();
                var uploadCompleted = 0;
                await Task.WhenAll(compared.Select(async row =>
                {
                    var result = uploadIndexes.Contains(row.Index)
                        ? await UploadAsync(row, stagingRoot, session, gate, cancellationToken)
                        : row with { State = S3StorageFileState.Skipped };
                    lock (sync)
                    {
                        uploadedRows[result.Index] = result;
                        if (uploadIndexes.Contains(result.Index)) uploadCompleted++;
                        view = view with { Files = uploadedRows.OrderBy(item => item.Index).ToArray(), UploadCompletedCount = uploadCompleted };
                        context.SetView(view);
                    }
                }));

                var failed = uploadedRows.Any(IsFailure);
                view = view with
                {
                    Files = uploadedRows,
                    Outcome = failed ? S3StorageOutcome.CompletedWithErrors : S3StorageOutcome.Completed,
                    Phase = failed ? "completed-with-errors" : "completed",
                    LastUploadedAtUtc = uploadedRows.Any(row => row.State == S3StorageFileState.Uploaded) ? DateTimeOffset.UtcNow : null,
                    WarningCode = failed ? "s3_partial_publication" : null
                };
                context.SetView(view);
                await SaveReceiptAsync(book.Directory, operation, view, startedAt, DateTimeOffset.UtcNow, cancellationToken);
                return view;
            }
            catch (OperationCanceledException)
            {
                var mayBePartial = request.Action == S3StorageAction.Upload && view.Phase == "publishing";
                view = view with { Outcome = S3StorageOutcome.Cancelled, Phase = "cancelled", WarningCode = mayBePartial ? "s3_partial_publication" : "s3_operation_cancelled" };
                context.SetView(view);
                await SaveReceiptBestEffortAsync(book.Directory, operation, view, startedAt, DateTimeOffset.UtcNow);
                throw;
            }
            catch (BackgroundTaskFailureException failure)
            {
                view = view with { Outcome = S3StorageOutcome.CompletedWithErrors, Phase = "failed", WarningCode = failure.Code };
                context.SetView(view);
                await SaveReceiptBestEffortAsync(book.Directory, operation, view, startedAt, DateTimeOffset.UtcNow);
                throw;
            }
            catch (S3StorageRemoteException failure)
            {
                view = view with { Outcome = S3StorageOutcome.CompletedWithErrors, Phase = "failed", WarningCode = failure.Code };
                context.SetView(view);
                await SaveReceiptBestEffortAsync(book.Directory, operation, view, startedAt, DateTimeOffset.UtcNow);
                throw new BackgroundTaskFailureException(failure.Code, failure.Message);
            }
            catch (Exception)
            {
                view = view with { Outcome = S3StorageOutcome.CompletedWithErrors, Phase = "failed", WarningCode = "background_task_failed" };
                context.SetView(view);
                await SaveReceiptBestEffortAsync(book.Directory, operation, view, startedAt, DateTimeOffset.UtcNow);
                throw;
            }
            finally
            {
                TryDeleteStaging(stagingRoot);
            }
        }
        finally { operationContexts.Remove(request.OperationContextId); }
    }

    private static async Task<S3StorageFileView> StageAsync(S3StorageFileView row, string outputRoot, string stagingRoot, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        var source = Path.Combine(outputRoot, row.FileName);
        if (!File.Exists(source)) return row with { State = S3StorageFileState.MissingLocal, ErrorCode = "s3_local_file_missing" };
        await gate.WaitAsync(cancellationToken);
        try
        {
            var destination = Path.Combine(stagingRoot, row.FileName);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, cancellationToken);
            var info = new FileInfo(destination);
            await using var hashStream = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken)).ToLowerInvariant();
            return row with { LocalLength = info.Length, LocalSha256 = hash, ErrorCode = null };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return row with { State = S3StorageFileState.Failed, ErrorCode = "s3_local_file_unavailable" };
        }
        finally { gate.Release(); }
    }

    private static async Task<S3StorageFileView> CompareAsync(S3StorageFileView row, IS3ObjectSession session, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        if (row.State is S3StorageFileState.MissingLocal or S3StorageFileState.Failed) return row;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var remote = await session.HeadAsync(row.ObjectKey, cancellationToken);
            if (remote is null) return row with { State = S3StorageFileState.MissingRemote };
            if (remote.Length != row.LocalLength || !string.Equals(remote.Sha256, row.LocalSha256, StringComparison.OrdinalIgnoreCase)) return row with { State = S3StorageFileState.Changed };
            return row with { State = remote.IsPublic ? S3StorageFileState.Synced : S3StorageFileState.SyncedButNotPublic };
        }
        catch (S3StorageRemoteException exception) { return row with { State = S3StorageFileState.Failed, ErrorCode = exception.Code }; }
        finally { gate.Release(); }
    }

    private static async Task<S3StorageFileView> UploadAsync(S3StorageFileView row, string stagingRoot, IS3ObjectSession session, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await session.PutAsync(row.ObjectKey, new FileReference(Path.Combine(stagingRoot, row.FileName)), row.ContentType, row.LocalSha256!, row.LocalLength!.Value, cancellationToken);
            var verified = await session.HeadAsync(row.ObjectKey, cancellationToken);
            return verified is not null && verified.IsPublic && verified.Length == row.LocalLength && string.Equals(verified.Sha256, row.LocalSha256, StringComparison.OrdinalIgnoreCase)
                ? row with { State = S3StorageFileState.Uploaded, ErrorCode = null }
                : row with { State = S3StorageFileState.Failed, ErrorCode = "s3_upload_verification_failed" };
        }
        catch (S3StorageRemoteException exception) { return row with { State = S3StorageFileState.Failed, ErrorCode = exception.Code }; }
        finally { gate.Release(); }
    }

    private ValueTask SaveReceiptAsync(DirectoryReference directory, S3StorageOperationContext operation, S3StorageBookView view, DateTimeOffset started, DateTimeOffset? finished, CancellationToken token) =>
        receiptStore.SaveAsync(directory, new(1, view.BookId, view.Asin, view.Action, view.Outcome, operation.ConfigurationRevision, view, started, DateTimeOffset.UtcNow, finished), token);

    private async ValueTask SaveReceiptBestEffortAsync(DirectoryReference directory, S3StorageOperationContext operation, S3StorageBookView view, DateTimeOffset started, DateTimeOffset? finished)
    {
        try { await SaveReceiptAsync(directory, operation, view, started, finished, CancellationToken.None); } catch { }
    }

    private static IReadOnlyList<S3StorageFileView> MergeRows(IReadOnlyList<S3StorageFileView> baseline, IReadOnlyList<S3StorageFileView?> updates) => baseline.Select((row, index) => updates[index] ?? row).ToArray();
    private static bool IsFailure(S3StorageFileView row) => row.State is S3StorageFileState.MissingLocal or S3StorageFileState.Failed;

    private static void EnsureCapacity(string outputRoot, string stagingRoot, IReadOnlyList<S3StorageFileView> rows)
    {
        var required = rows.Select(row => Path.Combine(outputRoot, row.FileName)).Where(File.Exists).Sum(path => new FileInfo(path).Length);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(stagingRoot))!);
        if (drive.AvailableFreeSpace < required) throw new BackgroundTaskFailureException("s3_staging_insufficient_space", "There is not enough disk space to prepare the S3 publication package.");
    }

    private static void EnsureContained(string appRoot, string stagingRoot)
    {
        var root = Path.GetFullPath(Path.Combine(appRoot, ".printablebook", "s3-staging")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new BackgroundTaskFailureException("s3_staging_path_invalid", "The S3 staging path escaped its application-owned root.");
    }

    private static void TryDeleteStaging(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
