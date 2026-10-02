using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.S3Storage;

public static partial class S3StoragePolicy
{
    public const string Sha256MetadataName = "sha256";

    public static string NormalizeAsin(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return ValidAsin().IsMatch(normalized) ? normalized : string.Empty;
    }

    public static IReadOnlyList<string> FileNames(string bookId) =>
    [
        $"{bookId} - Cover.pdf",
        $"{bookId} - Cover_thumbnail.png",
        $"{bookId} - Cover_thumbnail_back.png",
        $"{bookId} - Cover_thumbnail_front.png",
        $"{bookId} - Interior.pdf",
        $"{bookId} - Interior_thumbnail.png",
        $"{bookId} - Interior_thumbnail.pdf"
    ];

    public static string ObjectKey(string asin, string fileName) => $"{NormalizeRequiredAsin(asin)}/{fileName}";

    public static string PublicUrl(S3StorageSettings settings, string objectKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var baseUrl = string.IsNullOrWhiteSpace(settings.PublicBaseUrl)
            ? $"https://s3.{settings.Region}.amazonaws.com"
            : settings.PublicBaseUrl.TrimEnd('/');
        var encodedKey = string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));
        return $"{baseUrl}/{Uri.EscapeDataString(settings.Bucket)}/{encodedKey}";
    }

    public static S3StorageSettings Validate(S3StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var accessKey = settings.AccessKey?.Trim() ?? string.Empty;
        var secretKey = settings.SecretKey?.Trim() ?? string.Empty;
        var bucket = settings.Bucket?.Trim() ?? string.Empty;
        var region = settings.Region?.Trim() ?? string.Empty;
        var publicBaseUrl = string.IsNullOrWhiteSpace(settings.PublicBaseUrl) ? null : settings.PublicBaseUrl.Trim().TrimEnd('/');

        if (accessKey.Length == 0 || secretKey.Length == 0) throw new S3StorageValidationException("s3_credentials_required", "Access Key and Secret Key are required.");
        if (!ValidBucket().IsMatch(bucket)) throw new S3StorageValidationException("s3_bucket_invalid", "Enter a valid S3 bucket name.");
        if (!ValidRegion().IsMatch(region)) throw new S3StorageValidationException("s3_region_invalid", "Enter a valid AWS region.");
        if (publicBaseUrl is not null && (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))))
        {
            throw new S3StorageValidationException("s3_url_invalid", "URL must be an absolute HTTP or HTTPS address.");
        }
        return new(accessKey, secretKey, bucket, region, publicBaseUrl);
    }

    private static string NormalizeRequiredAsin(string value)
    {
        var normalized = NormalizeAsin(value);
        return normalized.Length == 0
            ? throw new S3StorageValidationException("s3_asin_invalid", "A 10-character alphanumeric ASIN is required.")
            : normalized;
    }

    [GeneratedRegex("^[A-Z0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidAsin();

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidBucket();

    [GeneratedRegex("^[a-z]{2}(?:-gov)?-[a-z0-9-]+-\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidRegion();
}

public sealed class S3StorageValidationException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public sealed class S3StorageRemoteException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed record S3StorageSettings(string AccessKey, string SecretKey, string Bucket, string Region, string? PublicBaseUrl);
public sealed record S3StorageSettingsInput(string? AccessKey, string? SecretKey, string Bucket, string Region, string? PublicBaseUrl);
public sealed record S3StorageConfiguration(string Bucket, string Region, string? PublicBaseUrl, bool HasCredentials);

public interface IS3StorageSettingsStore
{
    ValueTask<S3StorageSettings?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask<S3StorageConfiguration> SaveAsync(S3StorageSettingsInput input, CancellationToken cancellationToken = default);
}

public sealed record S3RemoteObject(string Sha256);

public interface IS3ObjectClient
{
    ValueTask<S3RemoteObject?> GetAsync(S3StorageSettings settings, string objectKey, CancellationToken cancellationToken = default);
    ValueTask UploadAsync(S3StorageSettings settings, string objectKey, FileReference source, string sha256, CancellationToken cancellationToken = default);
}

public enum S3StorageAction { Check, Upload }
public enum S3StorageOutcome { Pending, Running, Completed, CompletedWithErrors, Cancelled }
public enum S3StorageFileState { Pending, Missing, Existing, Changed, Uploaded, Skipped, Failed }

public sealed record S3StorageFileView(string FileName, string ObjectKey, string PublicUrl, S3StorageFileState State, string? ErrorCode = null);

public sealed record S3StorageBookView(
    string BookId,
    string Asin,
    S3StorageAction Action,
    S3StorageOutcome Outcome,
    IReadOnlyList<S3StorageFileView> Files,
    int CompletedCount,
    int TotalCount,
    DateTimeOffset? LastCheckedAtUtc = null,
    DateTimeOffset? LastUploadedAtUtc = null)
{
    public static S3StorageBookView Pending(string bookId, string asin, S3StorageAction action, S3StorageSettings settings) => new(
        bookId,
        asin,
        action,
        S3StorageOutcome.Pending,
        S3StoragePolicy.FileNames(bookId).Select(fileName => new S3StorageFileView(
            fileName,
            S3StoragePolicy.ObjectKey(asin, fileName),
            S3StoragePolicy.PublicUrl(settings, S3StoragePolicy.ObjectKey(asin, fileName)),
            S3StorageFileState.Pending)).ToArray(),
        0,
        S3StoragePolicy.FileNames(bookId).Count);
}

public sealed record S3StorageTaskRequest(string BookId, S3StorageAction Action);

public sealed record S3StorageSessionSnapshot(
    string? TaskId,
    string BookId,
    bool IsActive,
    bool IsCancelling,
    S3StorageBookView? View,
    string? ErrorCode = null);

public sealed record S3StorageBookOverview(
    string BookId,
    string Title,
    string? Asin,
    bool IsAsinValid,
    IReadOnlyList<string> MissingFiles,
    S3StorageSessionSnapshot? Session)
{
    public bool IsEligible => IsAsinValid && MissingFiles.Count == 0;
}

public sealed record S3StorageOverview(S3StorageConfiguration Configuration, IReadOnlyList<S3StorageBookOverview> Books);

public interface IS3StorageService
{
    ValueTask<S3StorageOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    ValueTask<S3StorageOverview> SaveSettingsAsync(S3StorageSettingsInput input, CancellationToken cancellationToken = default);
    ValueTask<S3StorageSessionSnapshot> StartAsync(string bookId, S3StorageAction action, CancellationToken cancellationToken = default);
    ValueTask<S3StorageSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default);
    ValueTask<S3StorageSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default);
}

public sealed class S3StorageService(
    IApplicationSnapshotProvider snapshotProvider,
    IS3StorageSettingsStore settingsStore,
    IBackgroundTaskManager taskManager) : IS3StorageService
{
    public async ValueTask<S3StorageOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var configuration = settings is null
            ? new S3StorageConfiguration(string.Empty, string.Empty, null, false)
            : new S3StorageConfiguration(settings.Bucket, settings.Region, settings.PublicBaseUrl, true);
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken);
        var books = snapshot.Discovery.Books.Select(book =>
        {
            var summary = snapshot.BookSummaries.First(item => item.BookId == book.Id);
            var asin = S3StoragePolicy.NormalizeAsin(summary.Metadata?.Asin);
            var outputDirectory = Path.Combine(book.Directory.Value, "Output");
            var missing = S3StoragePolicy.FileNames(book.Id.Value)
                .Where(fileName => !File.Exists(Path.Combine(outputDirectory, fileName)))
                .ToArray();
            var task = tasks.FirstOrDefault(item => string.Equals(item.Subject, book.Id.Value, StringComparison.Ordinal) && IsActive(item.State))
                ?? tasks.FirstOrDefault(item => string.Equals(item.Subject, book.Id.Value, StringComparison.Ordinal));
            return new S3StorageBookOverview(
                book.Id.Value,
                summary.Metadata?.Title ?? book.Name,
                asin.Length == 0 ? summary.Metadata?.Asin : asin,
                asin.Length > 0,
                missing,
                task is null ? null : ToSession(task));
        }).ToArray();
        return new(configuration, books);
    }

    public async ValueTask<S3StorageOverview> SaveSettingsAsync(S3StorageSettingsInput input, CancellationToken cancellationToken = default)
    {
        await settingsStore.SaveAsync(input, cancellationToken);
        return await GetOverviewAsync(cancellationToken);
    }

    public async ValueTask<S3StorageSessionSnapshot> StartAsync(string bookId, S3StorageAction action, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var book = snapshot.Discovery.Books.FirstOrDefault(item => item.Id.Value == bookId)
            ?? throw new S3StorageValidationException("book_not_found", "Book was not found.");
        var summary = snapshot.BookSummaries.First(item => item.BookId == book.Id);
        var asin = S3StoragePolicy.NormalizeAsin(summary.Metadata?.Asin);
        if (asin.Length == 0) throw new S3StorageValidationException("s3_asin_invalid", "A 10-character alphanumeric ASIN is required.");
        var settings = await settingsStore.LoadAsync(cancellationToken)
            ?? throw new S3StorageValidationException("s3_settings_required", "Save S3 settings before checking or uploading.");
        var initial = S3StorageBookView.Pending(bookId, asin, action, settings);
        var task = await taskManager.StartAsync(BackgroundTaskKind.S3Storage, bookId, bookId, new S3StorageTaskRequest(bookId, action), initial, cancellationToken);
        return ToSession(task, initial);
    }

    public async ValueTask<S3StorageSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default)
    {
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken);
        var task = tasks.FirstOrDefault(item => item.Subject == bookId && IsActive(item.State))
            ?? tasks.FirstOrDefault(item => item.Subject == bookId);
        return task is null ? new(null, bookId, false, false, null) : ToSession(task);
    }

    public async ValueTask<S3StorageSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default)
    {
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken);
        var task = tasks.FirstOrDefault(item => item.Subject == bookId && IsActive(item.State));
        if (task is null) return await GetAsync(bookId, cancellationToken);
        var cancelled = await taskManager.CancelAsync(task.TaskId, cancellationToken);
        return cancelled is null ? await GetAsync(bookId, cancellationToken) : ToSession(cancelled);
    }

    private S3StorageSessionSnapshot ToSession(BackgroundTaskSnapshot task, S3StorageBookView? fallback = null)
    {
        taskManager.TryGetView(task.TaskId, out S3StorageBookView? view);
        var effectiveView = view ?? fallback;
        if (effectiveView is not null && task.State == BackgroundTaskState.Cancelled)
        {
            effectiveView = effectiveView with { Outcome = S3StorageOutcome.Cancelled };
        }
        else if (effectiveView is not null && task.State == BackgroundTaskState.Failed)
        {
            effectiveView = effectiveView with { Outcome = S3StorageOutcome.CompletedWithErrors };
        }
        return new(task.TaskId.Value, task.Subject ?? string.Empty, IsActive(task.State), task.State == BackgroundTaskState.Cancelling, effectiveView, task.ErrorCode);
    }

    private static bool IsActive(BackgroundTaskState state) => state is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling;
}

public sealed class S3StorageWorker(
    IApplicationSnapshotProvider snapshotProvider,
    IS3StorageSettingsStore settingsStore,
    IS3ObjectClient objectClient) : BackgroundTaskWorker<S3StorageTaskRequest, S3StorageBookView>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.S3Storage;

    protected override async ValueTask<S3StorageBookView> ExecuteTypedAsync(S3StorageTaskRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
    {
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var book = snapshot.Discovery.Books.FirstOrDefault(item => item.Id.Value == request.BookId)
            ?? throw new BackgroundTaskFailureException("book_not_found", "Book was not found.");
        var summary = snapshot.BookSummaries.First(item => item.BookId == book.Id);
        var asin = S3StoragePolicy.NormalizeAsin(summary.Metadata?.Asin);
        if (asin.Length == 0) throw new BackgroundTaskFailureException("s3_asin_invalid", "A 10-character alphanumeric ASIN is required.");
        var settings = await settingsStore.LoadAsync(cancellationToken)
            ?? throw new BackgroundTaskFailureException("s3_settings_required", "Save S3 settings before checking or uploading.");
        var outputDirectory = Path.Combine(book.Directory.Value, "Output");
        var fileNames = S3StoragePolicy.FileNames(book.Id.Value);
        var missing = fileNames.Where(fileName => !File.Exists(Path.Combine(outputDirectory, fileName))).ToArray();
        if (missing.Length > 0) throw new BackgroundTaskFailureException("s3_output_files_missing", $"Required output files are missing: {string.Join(", ", missing)}");

        var view = S3StorageBookView.Pending(book.Id.Value, asin, request.Action, settings) with { Outcome = S3StorageOutcome.Running };
        context.SetView(view);
        var rows = view.Files.ToArray();
        var uploaded = false;

        for (var index = 0; index < rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            var source = new FileReference(Path.Combine(outputDirectory, row.FileName));
            context.Report(request.Action == S3StorageAction.Check ? "s3.check" : "s3.upload", index, rows.Length, row.FileName, book.Id.Value);
            try
            {
                var hash = await HashAsync(source, cancellationToken);
                var remote = await objectClient.GetAsync(settings, row.ObjectKey, cancellationToken);
                var state = remote is null
                    ? S3StorageFileState.Missing
                    : string.Equals(remote.Sha256, hash, StringComparison.OrdinalIgnoreCase)
                        ? S3StorageFileState.Existing
                        : S3StorageFileState.Changed;
                if (request.Action == S3StorageAction.Upload)
                {
                    if (state == S3StorageFileState.Existing)
                    {
                        state = S3StorageFileState.Skipped;
                    }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await objectClient.UploadAsync(settings, row.ObjectKey, source, hash, cancellationToken);
                        state = S3StorageFileState.Uploaded;
                        uploaded = true;
                    }
                }
                rows[index] = row with { State = state, ErrorCode = null };
            }
            catch (OperationCanceledException) { throw; }
            catch (S3StorageRemoteException exception)
            {
                rows[index] = row with { State = S3StorageFileState.Failed, ErrorCode = exception.Code };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                rows[index] = row with { State = S3StorageFileState.Failed, ErrorCode = "s3_local_file_unavailable" };
            }

            view = view with
            {
                Files = rows.ToArray(),
                CompletedCount = index + 1,
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
                LastUploadedAtUtc = uploaded ? DateTimeOffset.UtcNow : view.LastUploadedAtUtc
            };
            context.SetView(view);
        }

        view = view with { Outcome = rows.Any(row => row.State == S3StorageFileState.Failed) ? S3StorageOutcome.CompletedWithErrors : S3StorageOutcome.Completed };
        context.SetView(view);
        return view;
    }

    private static async ValueTask<string> HashAsync(FileReference file, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
