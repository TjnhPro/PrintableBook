using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.S3Storage;

public static partial class S3StoragePolicy
{
    public const string Sha256MetadataName = "printablebook-sha256";
    public const string LengthMetadataName = "printablebook-length";
    public const int MaximumFileConcurrency = 4;

    public static string NormalizeAsin(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return ValidAsin().IsMatch(normalized) ? normalized : string.Empty;
    }

    public static IReadOnlyList<string> FileNames(string bookId) => BookOutputArtifactContract.ForBook(bookId).Select(item => item.FileName).ToArray();

    public static S3StorageConfiguration NormalizeConfiguration(S3StorageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new(configuration.Region?.Trim().ToLowerInvariant() ?? string.Empty, configuration.Bucket?.Trim().ToLowerInvariant() ?? string.Empty, NormalizeFolder(configuration.Folder));
    }

    public static S3StorageConfiguration ValidateConfiguration(S3StorageConfiguration configuration)
    {
        var value = NormalizeConfiguration(configuration);
        if (!ValidBucket().IsMatch(value.Bucket)) throw new S3StorageValidationException("s3_bucket_invalid", "Enter a valid S3 bucket name.");
        if (!ValidRegion().IsMatch(value.Region)) throw new S3StorageValidationException("s3_region_invalid", "Enter a valid AWS region.");
        if (value.Folder.Length == 0) throw new S3StorageValidationException("s3_folder_invalid", "Enter a non-empty S3 destination folder.");
        return value;
    }

    public static S3StorageSettings Validate(S3StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var accessKey = settings.AccessKey?.Trim() ?? string.Empty;
        var secretKey = settings.SecretKey?.Trim() ?? string.Empty;
        if (accessKey.Length == 0 || secretKey.Length == 0) throw new S3StorageValidationException("s3_credentials_required", "Access Key and Secret Key are required.");
        return new(accessKey, secretKey, ValidateConfiguration(settings.Configuration));
    }

    public static string ObjectKey(string folder, string asin, string fileName)
    {
        var normalizedAsin = NormalizeAsin(asin);
        if (normalizedAsin.Length == 0) throw new S3StorageValidationException("s3_asin_invalid", "A 10-character alphanumeric ASIN is required.");
        var normalizedFolder = NormalizeFolder(folder);
        if (normalizedFolder.Length == 0) throw new S3StorageValidationException("s3_folder_invalid", "Enter a non-empty S3 destination folder.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." || fileName.IndexOfAny(['/', '\\']) >= 0 || fileName.Any(char.IsControl))
            throw new S3StorageValidationException("s3_file_name_invalid", "S3 publication file names must be a single safe path segment.");
        return $"{normalizedFolder}/{normalizedAsin}/{fileName}";
    }

    public static string DestinationPrefix(string folder, string asin)
    {
        var normalizedFolder = NormalizeFolder(folder);
        var normalizedAsin = NormalizeAsin(asin);
        if (normalizedFolder.Length == 0) throw new S3StorageValidationException("s3_folder_invalid", "Enter a non-empty S3 destination folder.");
        if (normalizedAsin.Length == 0) throw new S3StorageValidationException("s3_asin_invalid", "A 10-character alphanumeric ASIN is required.");
        return $"{normalizedFolder}/{normalizedAsin}/";
    }

    public static string PublicUrl(S3StorageSettings settings, string objectKey) => PublicUrl(settings.Configuration, objectKey);

    public static string PublicUrl(S3StorageConfiguration configuration, string objectKey)
    {
        var encodedKey = string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));
        return $"https://s3.dualstack.{configuration.Region}.amazonaws.com/{Uri.EscapeDataString(configuration.Bucket)}/{encodedKey}";
    }

    public static string ConfigurationRevision(S3StorageSettings settings, Guid generation)
    {
        var bytes = Encoding.UTF8.GetBytes($"{settings.Region}\n{settings.Bucket}\n{settings.Folder}\n{generation:N}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string NormalizeFolder(string? value)
    {
        var normalized = (value ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
        if (normalized.Length == 0) return string.Empty;
        var parts = normalized.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.Any(char.IsControl)))
            throw new S3StorageValidationException("s3_folder_invalid", "S3 folder cannot contain empty, dot, or control-character segments.");
        return string.Join('/', parts);
    }

    [GeneratedRegex("^[A-Z0-9]{10}$", RegexOptions.CultureInvariant)] private static partial Regex ValidAsin();
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)] private static partial Regex ValidBucket();
    [GeneratedRegex("^[a-z]{2}(?:-gov)?-[a-z0-9-]+-\\d+$", RegexOptions.CultureInvariant)] private static partial Regex ValidRegion();
}

public sealed class S3StorageValidationException(string code, string message) : ArgumentException(message) { public string Code { get; } = code; }
public sealed class S3StorageRemoteException(string code, string message, Exception? innerException = null) : Exception(message, innerException) { public string Code { get; } = code; }

public sealed record S3StorageConfiguration(string Region, string Bucket, string Folder)
{
    public static S3StorageConfiguration Default { get; } = new("us-east-1", string.Empty, "coloring");
}

public sealed record S3StorageSettings(string AccessKey, string SecretKey, S3StorageConfiguration Configuration)
{
    public string Region => Configuration.Region;
    public string Bucket => Configuration.Bucket;
    public string Folder => Configuration.Folder;
}

public sealed record S3StorageOperationSettings(S3StorageSettings Settings, Guid CredentialGeneration);
public sealed record S3StorageCredentialInput(string AccessKey, string SecretKey);
public enum S3CredentialStatus { NotConfigured, Configured, Unavailable }
public sealed record S3StorageConfigurationStatus(S3StorageConfiguration Configuration, S3CredentialStatus CredentialStatus, string? MaskedAccessKey = null, string? ErrorCode = null);

public interface IS3StorageSettingsStore
{
    ValueTask<S3StorageSettings?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask<S3StorageOperationSettings?> LoadOperationAsync(CancellationToken cancellationToken = default);
    ValueTask<S3StorageConfigurationStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    ValueTask<S3StorageConfigurationStatus> ReplaceCredentialsAsync(S3StorageCredentialInput input, CancellationToken cancellationToken = default);
}

public sealed record S3RemotePrefix(IReadOnlySet<string> ObjectKeys);
public sealed record S3RemoteObject(string Sha256, long Length, bool IsPublic, long? MetadataLength = null);
public interface IS3ObjectSession : IAsyncDisposable
{
    ValueTask<S3RemotePrefix> ListPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    ValueTask<S3RemoteObject?> HeadAsync(string objectKey, CancellationToken cancellationToken = default);
    ValueTask PutAsync(string objectKey, FileReference source, string contentType, string sha256, long length, CancellationToken cancellationToken = default);
}
public interface IS3ObjectSessionFactory { ValueTask<IS3ObjectSession> OpenAsync(S3StorageSettings settings, CancellationToken cancellationToken = default); }

public enum S3StorageAction { Check, Upload }
public enum S3StorageOutcome { Pending, Running, Completed, CompletedWithErrors, Cancelled, Interrupted }
public enum S3StorageFileState { Pending, MissingLocal, MissingRemote, Synced, SyncedButNotPublic, Changed, Uploaded, Skipped, Failed, Unknown }

public sealed record S3StorageFileView(
    int Index,
    string FileName,
    string ObjectKey,
    string PublicUrl,
    string ContentType,
    S3StorageFileState State,
    bool? LocalExists = null,
    long? LocalLength = null,
    string? LocalSha256 = null,
    bool? RemoteExists = null,
    long? RemoteLength = null,
    string? RemoteSha256 = null,
    bool? IsPublic = null,
    string? ErrorCode = null);
public sealed record S3StorageBookView(string BookId, string Asin, S3StorageAction Action, S3StorageOutcome Outcome, string Phase, IReadOnlyList<S3StorageFileView> Files, int CompletedCount, int TotalCount, int UploadCompletedCount = 0, int UploadTotalCount = 0, DateTimeOffset? LastCheckedAtUtc = null, DateTimeOffset? LastUploadedAtUtc = null, string? WarningCode = null)
{
    public static S3StorageBookView Pending(string bookId, string asin, S3StorageAction action, S3StorageSettings settings) => new(
        bookId, asin, action, S3StorageOutcome.Pending, "pending",
        BookOutputArtifactContract.ForBook(bookId).Select(artifact =>
        {
            var key = S3StoragePolicy.ObjectKey(settings.Folder, asin, artifact.FileName);
            return new S3StorageFileView(artifact.Index, artifact.FileName, key, S3StoragePolicy.PublicUrl(settings, key), artifact.ContentType, S3StorageFileState.Pending);
        }).ToArray(), 0, BookOutputArtifactContract.ForBook(bookId).Count);
}

public sealed record S3StorageTaskRequest(string BookId, S3StorageAction Action, Guid OperationContextId);
public sealed record S3StorageSessionSnapshot(string? TaskId, string BookId, bool IsActive, bool IsCancelling, S3StorageBookView? View, string? ErrorCode = null, string? ActiveBookId = null);
public sealed record S3StorageBookOverview(string BookId, string Title, string? Asin, bool IsAsinValid, IReadOnlyList<string> MissingFiles, S3StorageSessionSnapshot? Session) { public bool IsEligible => IsAsinValid && MissingFiles.Count == 0; }
public sealed record S3StorageOverview(S3StorageConfigurationStatus Configuration, IReadOnlyList<S3StorageBookOverview> Books, string? ActiveBookId = null);

public sealed record S3StorageOperationContext(Guid Id, S3StorageSettings Settings, Guid CredentialGeneration, string ConfigurationRevision);
public interface IS3StorageOperationContextStore
{
    S3StorageOperationContext Create(S3StorageSettings settings, Guid credentialGeneration);
    bool TryGet(Guid id, out S3StorageOperationContext? context);
    void Remove(Guid id);
}

public sealed class S3StorageOperationContextStore : IS3StorageOperationContextStore
{
    private readonly ConcurrentDictionary<Guid, S3StorageOperationContext> contexts = [];
    public S3StorageOperationContext Create(S3StorageSettings settings, Guid generation)
    {
        var value = new S3StorageOperationContext(Guid.NewGuid(), settings, generation, S3StoragePolicy.ConfigurationRevision(settings, generation));
        if (!contexts.TryAdd(value.Id, value)) throw new InvalidOperationException("Could not allocate an S3 operation context.");
        return value;
    }
    public bool TryGet(Guid id, out S3StorageOperationContext? context) => contexts.TryGetValue(id, out context);
    public void Remove(Guid id) => contexts.TryRemove(id, out _);
}

public interface IBookOutputLeaseCoordinator { ValueTask<IAsyncDisposable?> TryAcquireAsync(string outputPath, CancellationToken cancellationToken = default); }
public sealed class BookOutputLeaseCoordinator : IBookOutputLeaseCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> leases = new(StringComparer.OrdinalIgnoreCase);
    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));
        var gate = leases.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        return await gate.WaitAsync(TimeSpan.Zero, cancellationToken) ? new Releaser(gate) : null;
    }
    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int released;
        public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref released, 1) == 0) gate.Release(); return ValueTask.CompletedTask; }
    }
}

public sealed record S3PublicationReceipt(int Version, string BookId, string Asin, S3StorageConfiguration Destination, S3StorageAction Action, S3StorageOutcome Outcome, string ConfigurationRevision, S3StorageBookView View, DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? FinishedAtUtc = null);
public sealed record S3PublicationReceiptLoadResult(S3PublicationReceipt? Receipt, string? ErrorCode = null);
public interface IS3PublicationReceiptStore
{
    ValueTask<S3PublicationReceiptLoadResult> LoadAsync(DirectoryReference bookDirectory, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(DirectoryReference bookDirectory, S3PublicationReceipt receipt, CancellationToken cancellationToken = default);
}

public interface IS3StorageService
{
    ValueTask<S3StorageOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    ValueTask<S3StorageConfigurationStatus> ReplaceCredentialsAsync(S3StorageCredentialInput input, CancellationToken cancellationToken = default);
    ValueTask<S3StorageSessionSnapshot> StartAsync(string bookId, S3StorageAction action, CancellationToken cancellationToken = default);
    ValueTask<S3StorageSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default);
    ValueTask<S3StorageSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default);
}

public sealed class S3StorageService(IApplicationSnapshotProvider snapshotProvider, IS3StorageSettingsStore settingsStore, IS3StorageOperationContextStore operationContexts, IS3PublicationReceiptStore receiptStore, IBackgroundTaskManager taskManager) : IS3StorageService
{
    public async ValueTask<S3StorageOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var configuration = await settingsStore.GetStatusAsync(cancellationToken);
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken);
        var active = tasks.FirstOrDefault(item => IsActive(item.State));
        var books = new List<S3StorageBookOverview>(snapshot.Discovery.Books.Count);
        foreach (var book in snapshot.Discovery.Books)
        {
            var summary = snapshot.BookSummaries.First(item => item.BookId == book.Id);
            var asin = S3StoragePolicy.NormalizeAsin(summary.Metadata?.Asin);
            var output = Path.Combine(book.Directory.Value, "Output");
            var missing = S3StoragePolicy.FileNames(book.Id.Value).Where(file => !File.Exists(Path.Combine(output, file))).ToArray();
            var task = tasks.FirstOrDefault(item => item.Subject == book.Id.Value && IsActive(item.State)) ?? tasks.FirstOrDefault(item => item.Subject == book.Id.Value);
            S3StorageSessionSnapshot? session = task is null ? null : ToSession(task, active?.Subject);
            if (session is null)
            {
                var receipt = await receiptStore.LoadAsync(book.Directory, cancellationToken);
                if (receipt.Receipt is not null) session = new(null, book.Id.Value, false, false, HydratePublicUrls(receipt.Receipt), null, active?.Subject);
                else if (receipt.ErrorCode is not null) session = new(null, book.Id.Value, false, false, null, receipt.ErrorCode, active?.Subject);
            }
            books.Add(new(book.Id.Value, summary.Metadata?.Title ?? book.Name, asin.Length == 0 ? summary.Metadata?.Asin : asin, asin.Length > 0, missing, session));
        }
        return new(configuration, books, active?.Subject);
    }

    public ValueTask<S3StorageConfigurationStatus> ReplaceCredentialsAsync(S3StorageCredentialInput input, CancellationToken cancellationToken = default) => settingsStore.ReplaceCredentialsAsync(input, cancellationToken);

    public async ValueTask<S3StorageSessionSnapshot> StartAsync(string bookId, S3StorageAction action, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var book = snapshot.Discovery.Books.FirstOrDefault(item => item.Id.Value == bookId) ?? throw new S3StorageValidationException("book_not_found", "Book was not found.");
        var summary = snapshot.BookSummaries.First(item => item.BookId == book.Id);
        var asin = S3StoragePolicy.NormalizeAsin(summary.Metadata?.Asin);
        if (asin.Length == 0) throw new S3StorageValidationException("s3_asin_invalid", "A 10-character alphanumeric ASIN is required.");
        if (snapshot.BookSummaries.Count(item => S3StoragePolicy.NormalizeAsin(item.Metadata?.Asin) == asin) > 1) throw new S3StorageValidationException("s3_asin_duplicate", "Another Book uses the same ASIN. Assign a unique ASIN before publishing.");
        var captured = await settingsStore.LoadOperationAsync(cancellationToken) ?? throw new S3StorageValidationException("s3_settings_required", "Save S3 configuration and credentials before checking or uploading.");
        var operation = operationContexts.Create(captured.Settings, captured.CredentialGeneration);
        var initial = S3StorageBookView.Pending(bookId, asin, action, captured.Settings);
        try
        {
            var started = await taskManager.StartWithStatusAsync(BackgroundTaskKind.S3Storage, $"s3:{bookId}:{action}", bookId, new S3StorageTaskRequest(bookId, action, operation.Id), initial, cancellationToken);
            if (!started.WasCreated) operationContexts.Remove(operation.Id);
            return ToSession(started.Snapshot, bookId, initial);
        }
        catch { operationContexts.Remove(operation.Id); throw; }
    }

    public async ValueTask<S3StorageSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default)
    {
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken);
        var active = tasks.FirstOrDefault(item => IsActive(item.State));
        var task = tasks.FirstOrDefault(item => item.Subject == bookId && IsActive(item.State)) ?? tasks.FirstOrDefault(item => item.Subject == bookId);
        return task is null ? new(null, bookId, false, false, null, null, active?.Subject) : ToSession(task, active?.Subject);
    }

    public async ValueTask<S3StorageSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default)
    {
        var tasks = await taskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken);
        var task = tasks.FirstOrDefault(item => item.Subject == bookId && IsActive(item.State));
        if (task is null) return await GetAsync(bookId, cancellationToken);
        var result = await taskManager.CancelAsync(task.TaskId, cancellationToken);
        return result is null ? await GetAsync(bookId, cancellationToken) : ToSession(result, bookId);
    }

    private S3StorageSessionSnapshot ToSession(BackgroundTaskSnapshot task, string? activeBookId, S3StorageBookView? fallback = null)
    {
        taskManager.TryGetView(task.TaskId, out S3StorageBookView? value);
        var view = value ?? fallback;
        if (view is not null && task.State == BackgroundTaskState.Cancelled) view = view with { Outcome = S3StorageOutcome.Cancelled };
        else if (view is not null && task.State == BackgroundTaskState.Failed) view = view with { Outcome = S3StorageOutcome.CompletedWithErrors };
        return new(task.TaskId.Value, task.Subject ?? string.Empty, IsActive(task.State), task.State == BackgroundTaskState.Cancelling, view, task.ErrorCode, activeBookId);
    }

    private static S3StorageBookView HydratePublicUrls(S3PublicationReceipt receipt) => receipt.View with
    {
        Files = receipt.View.Files
            .Select(row => row with { PublicUrl = S3StoragePolicy.PublicUrl(receipt.Destination, row.ObjectKey) })
            .ToArray()
    };

    private static bool IsActive(BackgroundTaskState state) => state is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling;
}
