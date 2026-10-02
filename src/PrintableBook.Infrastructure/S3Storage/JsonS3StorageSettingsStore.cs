using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Infrastructure.S3Storage;

public sealed class JsonS3StorageSettingsStore(IApplicationRootDiscovery discovery, IFileSystem fileSystem) : IS3StorageSettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PrintableBook.S3Storage.v1");

    public async ValueTask<S3StorageSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var file = await GetFileAsync(cancellationToken);
        if (!await fileSystem.FileExistsAsync(file, cancellationToken)) return null;
        try
        {
            var persisted = JsonSerializer.Deserialize<PersistedSettings>(await fileSystem.ReadTextAsync(file, cancellationToken), Options);
            if (persisted is null) return null;
            if (persisted.Version != 1) throw new S3StorageValidationException("s3_settings_version_unsupported", "Stored S3 settings use an unsupported version.");
            return S3StoragePolicy.Validate(new(
                Unprotect(persisted.EncryptedAccessKey),
                Unprotect(persisted.EncryptedSecretKey),
                persisted.Bucket,
                persisted.Region,
                persisted.PublicBaseUrl));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or ArgumentException)
        {
            throw new S3StorageValidationException("s3_settings_unavailable", "Stored S3 settings could not be read. Save the settings again.");
        }
    }

    public async ValueTask<S3StorageConfiguration> SaveAsync(S3StorageSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var accessKey = input.AccessKey?.Trim() ?? string.Empty;
        var secretKey = input.SecretKey?.Trim() ?? string.Empty;
        if ((accessKey.Length == 0) != (secretKey.Length == 0))
        {
            throw new S3StorageValidationException("s3_credentials_incomplete", "Enter both Access Key and Secret Key.");
        }
        if (accessKey.Length == 0)
        {
            var current = await LoadAsync(cancellationToken)
                ?? throw new S3StorageValidationException("s3_credentials_required", "Access Key and Secret Key are required.");
            accessKey = current.AccessKey;
            secretKey = current.SecretKey;
        }

        var settings = S3StoragePolicy.Validate(new(accessKey, secretKey, input.Bucket, input.Region, input.PublicBaseUrl));
        var persisted = new PersistedSettings(
            1,
            settings.Bucket,
            settings.Region,
            settings.PublicBaseUrl,
            Protect(settings.AccessKey),
            Protect(settings.SecretKey));
        await fileSystem.WriteTextAtomicallyAsync(await GetFileAsync(cancellationToken), JsonSerializer.Serialize(persisted, Options), cancellationToken);
        return new(settings.Bucket, settings.Region, settings.PublicBaseUrl, true);
    }

    private async ValueTask<FileReference> GetFileAsync(CancellationToken cancellationToken)
    {
        var paths = (await discovery.DiscoverAsync(cancellationToken)).Paths;
        return new FileReference(Path.Combine(paths.Root.Value, "storage.json"));
    }

    private static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("S3 credential protection requires Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
    }

    private static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("S3 credential protection requires Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
    }

    private sealed record PersistedSettings(int Version, string Bucket, string Region, string? PublicBaseUrl, string EncryptedAccessKey, string EncryptedSecretKey);
}
