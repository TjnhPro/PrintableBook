using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.S3Storage;

namespace PrintableBook.Infrastructure.Tests.S3Storage;

public sealed class JsonS3StorageSettingsStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.S3Storage.{Guid.NewGuid():N}");

    [Fact]
    public async Task ReplaceCredentials_encrypts_secrets_and_load_combines_them_with_global_configuration()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        var settings = GlobalSettings.Default with { S3Storage = new("us-west-2", "valid-bucket", "coloring/books") };
        var store = new JsonS3StorageSettingsStore(new Discovery(CreatePaths()), new GlobalStore(settings), new PhysicalFileSystem());

        var status = await store.ReplaceCredentialsAsync(new("example-access", "example-secret"));
        var persisted = await File.ReadAllTextAsync(Path.Combine(root, "s3.credentials.dat"));
        var loaded = await store.LoadAsync();

        Assert.Equal(S3CredentialStatus.Configured, status.CredentialStatus);
        Assert.DoesNotContain("example-access", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("example-secret", persisted, StringComparison.Ordinal);
        Assert.Equal("example-access", loaded!.AccessKey);
        Assert.Equal("example-secret", loaded.SecretKey);
        Assert.Equal("valid-bucket", loaded.Bucket);
        Assert.Equal("us-west-2", loaded.Region);
        Assert.Equal("coloring/books", loaded.Folder);
    }

    [Fact]
    public async Task ReplaceCredentials_requires_both_fields_and_preserves_the_existing_envelope_on_failure()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        var store = new JsonS3StorageSettingsStore(new Discovery(CreatePaths()), new GlobalStore(GlobalSettings.Default with { S3Storage = new("us-east-1", "valid-bucket", "coloring") }), new PhysicalFileSystem());
        await store.ReplaceCredentialsAsync(new("first-access", "first-secret"));
        var before = await File.ReadAllTextAsync(Path.Combine(root, "s3.credentials.dat"));

        var failure = await Assert.ThrowsAsync<S3StorageValidationException>(() => store.ReplaceCredentialsAsync(new("", "replacement-secret")).AsTask());
        var after = await File.ReadAllTextAsync(Path.Combine(root, "s3.credentials.dat"));

        Assert.Equal("s3_credentials_incomplete", failure.Code);
        Assert.Equal(before, after);
        Assert.Equal("first-access", (await store.LoadAsync())!.AccessKey);
    }

    [Fact]
    public async Task Corrupt_encrypted_secret_reports_unavailable_without_exposing_ciphertext()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        var store = new JsonS3StorageSettingsStore(new Discovery(CreatePaths()), new GlobalStore(GlobalSettings.Default with { S3Storage = new("us-east-1", "valid-bucket", "coloring") }), new PhysicalFileSystem());
        await File.WriteAllTextAsync(
            Path.Combine(root, "s3.credentials.dat"),
            $$"""{"version":1,"generation":"{{Guid.NewGuid()}}","encryptedAccessKey":"not-base64","encryptedSecretKey":"also-not-base64"}""");

        var status = await store.GetStatusAsync();
        var failure = await Assert.ThrowsAsync<S3StorageValidationException>(() => store.LoadAsync().AsTask());

        Assert.Equal(S3CredentialStatus.Unavailable, status.CredentialStatus);
        Assert.Equal("s3_credentials_unavailable", status.ErrorCode);
        Assert.Null(status.MaskedAccessKey);
        Assert.Equal("s3_credentials_unavailable", failure.Code);
        Assert.DoesNotContain("not-base64", failure.Message, StringComparison.Ordinal);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
    }

    private ApplicationPaths CreatePaths() => new(
        new DirectoryReference(root),
        new DirectoryReference(Path.Combine(root, "brands")),
        new DirectoryReference(Path.Combine(root, "sources")),
        new FileReference(Path.Combine(root, "settings.json")));

    private sealed class Discovery(ApplicationPaths paths) : IApplicationRootDiscovery
    {
        public ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDiscovery(paths, [], []));
    }

    private sealed class GlobalStore(GlobalSettings settings) : IGlobalSettingsStore
    {
        public ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(settings);
        public ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default) => ValueTask.FromResult(settings);
        public ValueTask SaveAsync(GlobalSettings value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
