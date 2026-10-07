using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Infrastructure.Brands;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PhysicalBrandCloneServiceTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BrandClone.{Guid.NewGuid():N}");

    [Fact]
    public async Task CloneAsync_copies_all_nested_bytes_rewrites_metadata_and_excludes_only_root_validation()
    {
        var (paths, source) = CreateSource("ColoringWorld_de");
        var sourceNested = Path.Combine(source.Directory.Value, "custom", "nested");
        Directory.CreateDirectory(sourceNested);
        var expectedBytes = new byte[] { 0, 1, 2, 127, 128, 255 };
        await File.WriteAllBytesAsync(Path.Combine(sourceNested, "unknown.asset"), expectedBytes);
        await File.WriteAllTextAsync(Path.Combine(source.Directory.Value, "brand.metadata.json"), "{\"author\":\"Jane Doe\"}");
        await File.WriteAllTextAsync(Path.Combine(source.Directory.Value, "brand.validation.json"), "root certificate");
        await File.WriteAllTextAsync(Path.Combine(sourceNested, "brand.validation.json"), "nested user asset");

        var result = await new PhysicalBrandCloneService().CloneAsync(paths, source, Language("fr"));

        var destination = Path.Combine(paths.BrandsDirectory.Value, "ColoringWorld_fr");
        Assert.Equal("ColoringWorld_fr", result.DestinationBrand.Name);
        Assert.Equal(Path.GetFullPath(destination), result.DestinationBrand.Directory.Value);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(Path.Combine(destination, "custom", "nested", "unknown.asset")));
        var metadata = await new JsonBrandMetadataStore(new PrintableBook.Infrastructure.FileSystem.PhysicalFileSystem())
            .LoadAsync(new DirectoryReference(destination));
        Assert.Equal("Jane Doe", metadata!.Author);
        Assert.Equal("fr", metadata.LanguageCode);
        Assert.False(File.Exists(Path.Combine(destination, "brand.validation.json")));
        Assert.Equal("nested user asset", await File.ReadAllTextAsync(Path.Combine(destination, "custom", "nested", "brand.validation.json")));

        File.Delete(Path.Combine(destination, "custom", "nested", "unknown.asset"));
        await File.WriteAllTextAsync(Path.Combine(destination, "brand.metadata.json"), "changed");
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(Path.Combine(sourceNested, "unknown.asset")));
        Assert.Equal("{\"author\":\"Jane Doe\"}", await File.ReadAllTextAsync(Path.Combine(source.Directory.Value, "brand.metadata.json")));
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_creates_target_metadata_when_source_metadata_is_missing()
    {
        var (paths, source) = CreateSource("ColoringWorld");

        await new PhysicalBrandCloneService().CloneAsync(paths, source, Language("de"));

        var destination = new DirectoryReference(Path.Combine(paths.BrandsDirectory.Value, "ColoringWorld_de"));
        var metadata = await new JsonBrandMetadataStore(new PrintableBook.Infrastructure.FileSystem.PhysicalFileSystem())
            .LoadAsync(destination);
        Assert.Null(metadata!.Author);
        Assert.Equal("de", metadata.LanguageCode);
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_rejects_invalid_source_metadata_without_publishing()
    {
        var (paths, source) = CreateSource("ColoringWorld");
        await File.WriteAllTextAsync(
            Path.Combine(source.Directory.Value, "brand.metadata.json"),
            "{\"author\":\"Jane\",\"languageCode\":\"xx\"}");

        var exception = await Assert.ThrowsAsync<BrandCloneException>(() =>
            new PhysicalBrandCloneService().CloneAsync(paths, source, Language("de")).AsTask());

        Assert.Equal("brand_clone_failed", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(paths.BrandsDirectory.Value, "ColoringWorld_de")));
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_rejects_a_case_insensitive_destination_collision_without_modifying_it()
    {
        var (paths, source) = CreateSource("ColoringWorld_de");
        var collision = Path.Combine(paths.BrandsDirectory.Value, "COLORINGWORLD_FR");
        Directory.CreateDirectory(collision);
        await File.WriteAllTextAsync(Path.Combine(collision, "existing.txt"), "keep");

        var exception = await Assert.ThrowsAsync<BrandCloneException>(() =>
            new PhysicalBrandCloneService().CloneAsync(paths, source, Language("fr")).AsTask());

        Assert.Equal("brand_clone_destination_exists", exception.Code);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(collision, "existing.txt")));
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_rejects_invalid_language_and_missing_source_without_creating_a_destination()
    {
        var (paths, source) = CreateSource("ColoringWorld");
        var service = new PhysicalBrandCloneService();

        var invalid = await Assert.ThrowsAsync<BrandCloneException>(() =>
            service.CloneAsync(paths, source, new SupportedLanguageOption("xx", "Unknown")).AsTask());
        Directory.Delete(source.Directory.Value, recursive: true);
        var missing = await Assert.ThrowsAsync<BrandCloneException>(() =>
            service.CloneAsync(paths, source, Language("de")).AsTask());

        Assert.Equal("brand_clone_language_invalid", invalid.Code);
        Assert.Equal("brand_clone_source_not_found", missing.Code);
        Assert.False(Directory.Exists(Path.Combine(paths.BrandsDirectory.Value, "ColoringWorld_de")));
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_cleans_staging_when_copy_fails_or_is_cancelled()
    {
        var (paths, source) = CreateSource("ColoringWorld");
        await File.WriteAllTextAsync(Path.Combine(source.Directory.Value, "asset.bin"), "asset");
        var failing = new PhysicalBrandCloneService((_, _, _) => ValueTask.FromException(new IOException("simulated copy failure")));

        var failure = await Assert.ThrowsAsync<BrandCloneException>(() => failing.CloneAsync(paths, source, Language("de")).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PhysicalBrandCloneService().CloneAsync(paths, source, Language("fr"), cancellation.Token).AsTask());

        Assert.Equal("brand_clone_failed", failure.Code);
        Assert.False(Directory.Exists(Path.Combine(paths.BrandsDirectory.Value, "ColoringWorld_de")));
        Assert.False(Directory.Exists(Path.Combine(paths.BrandsDirectory.Value, "ColoringWorld_fr")));
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_rejects_a_reparse_point_and_cleans_staging()
    {
        var (paths, source) = CreateSource("ColoringWorld");
        var external = Path.Combine(rootPath, "external");
        Directory.CreateDirectory(external);
        await File.WriteAllTextAsync(Path.Combine(external, "outside.txt"), "outside");
        var link = Path.Combine(source.Directory.Value, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, external);
        }
        catch (Exception createException) when (createException is UnauthorizedAccessException or IOException)
        {
            return;
        }

        var exception = await Assert.ThrowsAsync<BrandCloneException>(() =>
            new PhysicalBrandCloneService().CloneAsync(paths, source, Language("de")).AsTask());

        Assert.Equal("brand_clone_failed", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(paths.BrandsDirectory.Value, "ColoringWorld_de")));
        Assert.Empty(StagingDirectories());
    }

    private (ApplicationPaths Paths, DiscoveredBrand Source) CreateSource(string name)
    {
        var brands = Path.Combine(rootPath, "brands");
        var sources = Path.Combine(rootPath, "sources");
        Directory.CreateDirectory(brands);
        Directory.CreateDirectory(sources);
        var sourcePath = Path.Combine(brands, name);
        Directory.CreateDirectory(sourcePath);
        var paths = new ApplicationPaths(new DirectoryReference(rootPath), new DirectoryReference(brands), new DirectoryReference(sources), new FileReference(Path.Combine(rootPath, "settings.json")));
        return (paths, new DiscoveredBrand(name, new DirectoryReference(sourcePath)));
    }

    private static SupportedLanguageOption Language(string code)
    {
        Assert.True(SupportedLanguageCatalog.TryGet(code, out var language));
        return language;
    }

    private string[] StagingDirectories() => Directory.Exists(rootPath)
        ? Directory.GetDirectories(rootPath, ".brand-clone-*", SearchOption.TopDirectoryOnly)
        : [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
