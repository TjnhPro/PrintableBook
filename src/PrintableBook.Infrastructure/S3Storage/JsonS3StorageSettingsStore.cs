using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Infrastructure.S3Storage;

public sealed class JsonS3StorageSettingsStore(IApplicationRootDiscovery discovery, IGlobalSettingsStore globalSettingsStore, IFileSystem fileSystem) : IS3StorageSettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PrintableBook.S3Storage.Credentials.v1");

    public async ValueTask<S3StorageSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
        (await LoadOperationAsync(cancellationToken))?.Settings;

    public async ValueTask<S3StorageOperationSettings?> LoadOperationAsync(CancellationToken cancellationToken = default)
    {
        var configuration = S3StoragePolicy.ValidateConfiguration((await globalSettingsStore.LoadAsync(cancellationToken)).EffectiveS3Storage);
        var envelope = await LoadEnvelopeAsync(cancellationToken);
        return envelope is null
            ? null
            : new(S3StoragePolicy.Validate(new(Unprotect(envelope.EncryptedAccessKey), Unprotect(envelope.EncryptedSecretKey), configuration)), envelope.Generation);
    }

    public async ValueTask<S3StorageConfigurationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var configuration = (await globalSettingsStore.LoadAsync(cancellationToken)).EffectiveS3Storage;
        try
        {
            var envelope = await LoadEnvelopeAsync(cancellationToken);
            if (envelope is null) return new(configuration, S3CredentialStatus.NotConfigured);
            var accessKey = Unprotect(envelope.EncryptedAccessKey);
            var secretKey = Unprotect(envelope.EncryptedSecretKey);
            if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
                throw new S3StorageValidationException("s3_credentials_unavailable", "Stored S3 credentials are incomplete. Replace the credentials.");
            return new(configuration, S3CredentialStatus.Configured, Mask(accessKey));
        }
        catch (S3StorageValidationException exception)
        {
            return new(configuration, S3CredentialStatus.Unavailable, null, exception.Code);
        }
    }

    public async ValueTask<S3StorageConfigurationStatus> ReplaceCredentialsAsync(S3StorageCredentialInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var accessKey = input.AccessKey?.Trim() ?? string.Empty;
        var secretKey = input.SecretKey?.Trim() ?? string.Empty;
        if (accessKey.Length == 0 || secretKey.Length == 0) throw new S3StorageValidationException("s3_credentials_incomplete", "Enter both Access Key and Secret Key.");
        var envelope = new CredentialEnvelope(1, Guid.NewGuid(), Protect(accessKey), Protect(secretKey));
        await fileSystem.WriteTextAtomicallyAsync(await GetFileAsync(cancellationToken), JsonSerializer.Serialize(envelope, Options), cancellationToken);
        return new((await globalSettingsStore.LoadAsync(cancellationToken)).EffectiveS3Storage, S3CredentialStatus.Configured, Mask(accessKey));
    }

    private async ValueTask<CredentialEnvelope?> LoadEnvelopeAsync(CancellationToken cancellationToken)
    {
        var file = await GetFileAsync(cancellationToken);
        if (!await fileSystem.FileExistsAsync(file, cancellationToken)) return null;
        try
        {
            var value = JsonSerializer.Deserialize<CredentialEnvelope>(await fileSystem.ReadTextAsync(file, cancellationToken), Options);
            if (value is null || value.Version != 1 || value.Generation == Guid.Empty) throw new S3StorageValidationException("s3_credentials_version_unsupported", "Stored S3 credentials use an unsupported format.");
            return value;
        }
        catch (S3StorageValidationException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or ArgumentException)
        {
            throw new S3StorageValidationException("s3_credentials_unavailable", "Stored S3 credentials could not be read. Replace the credentials.");
        }
    }

    private async ValueTask<FileReference> GetFileAsync(CancellationToken token) => new(Path.Combine((await discovery.DiscoverAsync(token)).Paths.Root.Value, "s3.credentials.dat"));
    private static string Mask(string value) => value.Length <= 4 ? "••••" : $"{value[..Math.Min(4, value.Length)]}••••{value[^Math.Min(4, value.Length)..]}";
    private static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("S3 credential protection requires Windows.");
        var plainBytes = Encoding.UTF8.GetBytes(value);
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("S3 credential protection requires Windows.");
        byte[]? protectedBytes = null;
        byte[]? plainBytes = null;
        try
        {
            protectedBytes = Convert.FromBase64String(value);
            plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            throw new S3StorageValidationException("s3_credentials_unavailable", "Stored S3 credentials could not be decrypted. Replace the credentials.");
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private sealed record CredentialEnvelope(int Version, Guid Generation, string EncryptedAccessKey, string EncryptedSecretKey);
}
