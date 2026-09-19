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
            BrandValidationRecord.CurrentSchemaVersion,
            BrandFingerprintCalculator.AssetFingerprintFormatVersion,
            new DateTimeOffset(2026, 8, 31, 4, 32, 0, TimeSpan.Zero),
            "sha256:definition",
            "sha256:abc",
            new DateTimeOffset(2026, 8, 31, 5, 0, 0, TimeSpan.Zero),
            RequiresValidation: false,
            [new("introtemplate/intro.png", new ImageSize(1024, 1024))]);
        var store = CreateStore();

        await store.SaveAsync(BrandDirectory(), expected);
        var actual = await store.LoadAsync(BrandDirectory());

        Assert.NotNull(actual);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.AssetFingerprintFormatVersion, actual.AssetFingerprintFormatVersion);
        Assert.Equal(expected.DefinitionChangedAtUtc, actual.DefinitionChangedAtUtc);
        Assert.Equal(expected.DefinitionSignature, actual.DefinitionSignature);
        Assert.Equal(expected.Fingerprint, actual.Fingerprint);
        Assert.Equal(expected.ValidatedAtUtc, actual.ValidatedAtUtc);
        Assert.Equal(expected.RequiresValidation, actual.RequiresValidation);
        Assert.Equal(expected.Assets, actual.Assets);
        var json = await File.ReadAllTextAsync(Path.Combine(BrandDirectory().Value, "brand.validation.json"));
        Assert.Contains("definitionChangedAtUtc", json, StringComparison.Ordinal);
        Assert.Contains("definitionSignature", json, StringComparison.Ordinal);
        Assert.Contains("assets", json, StringComparison.Ordinal);
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

    [Fact]
    public async Task LoadAsync_deserializes_a_legacy_record_as_schema_zero_for_fail_closed_migration()
    {
        Directory.CreateDirectory(BrandDirectory().Value);
        await File.WriteAllTextAsync(
            Path.Combine(BrandDirectory().Value, "brand.validation.json"),
            """
            {
              "definitionChangedAtUtc": "2026-09-10T00:00:00+00:00",
              "fingerprint": "sha256:legacy",
              "validatedAtUtc": "2026-09-18T03:24:35+00:00",
              "requiresValidation": false
            }
            """);

        var record = await CreateStore().LoadAsync(BrandDirectory());

        Assert.NotNull(record);
        Assert.Equal(0, record.SchemaVersion);
        Assert.Equal(0, record.AssetFingerprintFormatVersion);
        Assert.Null(record.DefinitionSignature);
        Assert.Null(record.Assets);
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
