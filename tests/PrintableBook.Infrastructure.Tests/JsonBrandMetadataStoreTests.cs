using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Infrastructure.Brands;
using PrintableBook.Infrastructure.FileSystem;

namespace PrintableBook.Infrastructure.Tests;

public sealed class JsonBrandMetadataStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.BrandMetadata.{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_returns_null_when_metadata_is_missing() =>
        Assert.Null(await Store().LoadAsync(BrandDirectory()));

    [Fact]
    public async Task SaveAsync_round_trips_trimmed_author_in_brand_local_file()
    {
        await Store().SaveAsync(BrandDirectory(), BrandMetadata.Create(" Jane Doe "));

        var restored = await Store().LoadAsync(BrandDirectory());
        var json = await File.ReadAllTextAsync(Path.Combine(BrandDirectory().Value, "brand.metadata.json"));
        Assert.Equal("Jane Doe", restored!.Author);
        Assert.Contains("\"author\": \"Jane Doe\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(".tmp", string.Join('|', Directory.EnumerateFiles(BrandDirectory().Value)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_rejects_malformed_json()
    {
        Directory.CreateDirectory(BrandDirectory().Value);
        await File.WriteAllTextAsync(Path.Combine(BrandDirectory().Value, "brand.metadata.json"), "{");

        await Assert.ThrowsAsync<JsonException>(async () => await Store().LoadAsync(BrandDirectory()));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
    }

    private DirectoryReference BrandDirectory() => new(Path.Combine(root, "Brand"));
    private static JsonBrandMetadataStore Store() => new(new PhysicalFileSystem());
}
