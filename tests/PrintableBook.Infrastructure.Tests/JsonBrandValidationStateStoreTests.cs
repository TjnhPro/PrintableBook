using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Infrastructure.BrandValidation;
using PrintableBook.Infrastructure.FileSystem;

namespace PrintableBook.Infrastructure.Tests;

public sealed class JsonBrandValidationStateStoreTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BrandValidation.{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_returns_null_when_state_file_is_missing()
    {
        var record = await CreateStore().LoadAsync(BrandDirectory());

        Assert.Null(record);
    }

    [Fact]
    public async Task SaveAsync_round_trips_record_as_atomic_brand_local_json()
    {
        var expected = new BrandValidationRecord(
            new DateTimeOffset(2026, 8, 31, 4, 32, 0, TimeSpan.Zero),
            "sha256:abc",
            new DateTimeOffset(2026, 8, 31, 5, 0, 0, TimeSpan.Zero),
            RequiresValidation: false);
        var store = CreateStore();

        await store.SaveAsync(BrandDirectory(), expected);
        var actual = await store.LoadAsync(BrandDirectory());

        Assert.Equal(expected, actual);
        var json = await File.ReadAllTextAsync(Path.Combine(BrandDirectory().Value, "brand.validation.json"));
        Assert.Contains("definitionChangedAtUtc", json, StringComparison.Ordinal);
        Assert.Contains("validatedAtUtc", json, StringComparison.Ordinal);
        Assert.DoesNotContain(".tmp", string.Join("|", Directory.EnumerateFiles(BrandDirectory().Value)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_does_not_treat_malformed_json_as_a_valid_record()
    {
        Directory.CreateDirectory(BrandDirectory().Value);
        await File.WriteAllTextAsync(Path.Combine(BrandDirectory().Value, "brand.validation.json"), "{");

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () => await CreateStore().LoadAsync(BrandDirectory()));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private DirectoryReference BrandDirectory() => new(Path.Combine(rootPath, "Brand"));
    private static JsonBrandValidationStateStore CreateStore() => new(new PhysicalFileSystem());
}
