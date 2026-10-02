using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.S3Storage;

namespace PrintableBook.Infrastructure.Tests.S3Storage;

public sealed class JsonS3StorageSettingsStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.S3Storage.{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_encrypts_credentials_and_blank_credentials_preserve_them()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        var store = new JsonS3StorageSettingsStore(new Discovery(CreatePaths()), new PhysicalFileSystem());

        var saved = await store.SaveAsync(new("example-access", "example-secret", "valid-bucket", "us-east-1", null));
        var persisted = await File.ReadAllTextAsync(Path.Combine(root, "storage.json"));
        var loaded = await store.LoadAsync();

        Assert.True(saved.HasCredentials);
        Assert.DoesNotContain("example-access", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("example-secret", persisted, StringComparison.Ordinal);
        Assert.Equal("example-access", loaded!.AccessKey);
        Assert.Equal("example-secret", loaded.SecretKey);

        await store.SaveAsync(new("", "", "next-bucket", "us-west-2", "https://s3.us-west-2.amazonaws.com"));
        loaded = await store.LoadAsync();
        Assert.Equal("example-access", loaded!.AccessKey);
        Assert.Equal("example-secret", loaded.SecretKey);
        Assert.Equal("next-bucket", loaded.Bucket);
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
}
