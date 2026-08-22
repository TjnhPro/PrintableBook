using PrintableBook.Core.Abstractions;
using PrintableBook.Infrastructure.Discovery;
using PrintableBook.Infrastructure.FileSystem;

namespace PrintableBook.Infrastructure.Tests;

public sealed class JsonBrandSettingsStoreTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BrandSettings.{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadAsync_persists_an_extensible_json_object_inside_the_brand_folder()
    {
        var directory = new DirectoryReference(Path.Combine(rootPath, "Amazon"));
        var store = new JsonBrandSettingsStore(new PhysicalFileSystem());

        await store.SaveAsync(directory, """{"frameEnabled":true,"future":{"key":"value"}}""");

        var json = await store.LoadAsync(directory);
        Assert.Contains("frameEnabled", json, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(directory.Value, "brand.json")));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
