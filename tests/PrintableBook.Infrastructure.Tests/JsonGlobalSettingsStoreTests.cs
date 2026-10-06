using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Discovery;
using PrintableBook.Infrastructure.FileSystem;

namespace PrintableBook.Infrastructure.Tests;

public sealed class JsonGlobalSettingsStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.Settings.{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_legacy_json_ignores_retired_interior_pdf_dimensions_and_materializes_detection_defaults()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(paths.SettingsFile.Value, "{\"maximumPageConcurrency\":4,\"artworkDetectionThreshold\":20,\"artworkMaximumSide\":2270,\"workingPageWidth\":2550,\"workingPageHeight\":2550,\"finalPageWidth\":2588,\"finalPageHeight\":2625,\"dpi\":300,\"interiorPdfWidthInches\":8.5,\"interiorPdfHeightInches\":8.5}");

        var loaded = await CreateStore(paths).LoadAsync(paths);

        Assert.Equal(2048, loaded.EffectiveArtworkSourceNormalization.NormalizedSourceSize);
        Assert.Equal(320, loaded.EffectiveBorderLineDetection.Pass2SearchDepth);
        Assert.NotNull(loaded.ArtworkSourceNormalization);
        Assert.NotNull(loaded.BorderLineDetection);

        await CreateStore(paths).SaveAsync(loaded);
        var saved = await File.ReadAllTextAsync(paths.SettingsFile.Value);
        Assert.DoesNotContain("interiorPdfWidthInches", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("interiorPdfHeightInches", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("finalInteriorPdfPageSize", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_round_trips_nested_detection_groups()
    {
        var paths = CreatePaths();
        var store = CreateStore(paths);
        var settings = GlobalSettings.Default with
        {
            ArtworkSourceNormalization = new ArtworkSourceNormalizationSettings(4096),
            BorderLineDetection = BorderLineDetectionSettings.Default with { Pass1SearchDepth = 250, Pass2SearchDepth = 500 }
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync(paths);

        Assert.Equal(settings.ArtworkSourceNormalization, loaded.ArtworkSourceNormalization);
        Assert.Equal(settings.BorderLineDetection, loaded.BorderLineDetection);
    }

    [Fact]
    public async Task SaveAsync_normalizes_and_round_trips_generic_keywords()
    {
        var paths = CreatePaths();
        var store = CreateStore(paths);

        await store.SaveAsync(GlobalSettings.Default with
        {
            GenericKeywords = [" coloring\tbooks ", "COLORING BOOKS", " ", "books for adults"]
        });
        var loaded = await store.LoadAsync(paths);

        Assert.Equal(["coloring books", "books for adults"], loaded.GenericKeywords);
        Assert.Equal(["coloring books", "books for adults"], loaded.GetEffectiveGenericKeywords("en"));
        Assert.Empty(loaded.GetEffectiveGenericKeywords("de"));
    }

    [Fact]
    public async Task LoadAsync_maps_legacy_generic_keywords_to_English_without_rewriting_the_file()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(root);
        const string legacy = "{\"maximumPageConcurrency\":4,\"artworkDetectionThreshold\":20,\"artworkMaximumSide\":2270,\"workingPageWidth\":2550,\"workingPageHeight\":2550,\"finalPageWidth\":2588,\"finalPageHeight\":2625,\"dpi\":300,\"genericKeywords\":[\" calm  coloring \",\"CALM COLORING\"]}";
        await File.WriteAllTextAsync(paths.SettingsFile.Value, legacy);

        var loaded = await CreateStore(paths).LoadAsync(paths);

        Assert.Equal(["calm coloring"], loaded.GetEffectiveGenericKeywords("en"));
        Assert.Empty(loaded.GetEffectiveGenericKeywords("fr"));
        Assert.Equal(legacy, await File.ReadAllTextAsync(paths.SettingsFile.Value));
    }

    [Fact]
    public async Task SaveAsync_round_trips_independent_language_profiles_and_mirrors_English_for_rollback()
    {
        var paths = CreatePaths();
        var store = CreateStore(paths);
        var profiles = new Dictionary<string, IReadOnlyList<string>>
        {
            ["en"] = [" English generic "],
            ["de"] = [" German generic "],
            ["ja"] = [" Japanese generic "]
        };

        await store.SaveAsync(GlobalSettings.Default with
        {
            GenericKeywords = ["stale legacy value"],
            GenericKeywordsByLanguage = profiles
        });
        var loaded = await store.LoadAsync(paths);

        Assert.Equal(["English generic"], loaded.GenericKeywords);
        Assert.Equal(["English generic"], loaded.GetEffectiveGenericKeywords("en"));
        Assert.Equal(["German generic"], loaded.GetEffectiveGenericKeywords("de"));
        Assert.Equal(["Japanese generic"], loaded.GetEffectiveGenericKeywords("ja"));
        Assert.Empty(loaded.GetEffectiveGenericKeywords("pt"));
        Assert.Equal(8, loaded.EffectiveGenericKeywordsByLanguage.Count);

        var persisted = await File.ReadAllTextAsync(paths.SettingsFile.Value);
        Assert.Contains("\"genericKeywords\"", persisted, StringComparison.Ordinal);
        Assert.Contains("\"genericKeywordsByLanguage\"", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_prefers_legacy_English_when_both_storage_contracts_exist()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(paths.SettingsFile.Value,
            "{\"maximumPageConcurrency\":4,\"artworkDetectionThreshold\":20,\"artworkMaximumSide\":2270,\"workingPageWidth\":2550,\"workingPageHeight\":2550,\"finalPageWidth\":2588,\"finalPageHeight\":2625,\"dpi\":300,\"genericKeywords\":[\"rollback edit\"],\"genericKeywordsByLanguage\":{\"en\":[\"old map value\"],\"de\":[\"German value\"]}} ");

        var loaded = await CreateStore(paths).LoadAsync(paths);

        Assert.Equal(["rollback edit"], loaded.GetEffectiveGenericKeywords("en"));
        Assert.Equal(["German value"], loaded.GetEffectiveGenericKeywords("de"));
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("")]
    public async Task SaveAsync_rejects_unsupported_language_profiles(string languageCode)
    {
        var paths = CreatePaths();
        var settings = GlobalSettings.Default with
        {
            GenericKeywordsByLanguage = new Dictionary<string, IReadOnlyList<string>> { [languageCode] = ["value"] }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => CreateStore(paths).SaveAsync(settings).AsTask());
        Assert.False(File.Exists(paths.SettingsFile.Value));
    }

    [Fact]
    public async Task SaveAsync_round_trips_s3_configuration_without_changing_unrelated_settings()
    {
        var paths = CreatePaths();
        var store = CreateStore(paths);
        var settings = GlobalSettings.Default with
        {
            MaximumPageConcurrency = 7,
            GenericKeywords = ["calm coloring"],
            S3Storage = new("US-EAST-1", "VXGROUP.TINH", "/coloring/books/")
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync(paths);

        Assert.Equal(7, loaded.MaximumPageConcurrency);
        Assert.Equal(["calm coloring"], loaded.GenericKeywords);
        Assert.Equal(new("us-east-1", "vxgroup.tinh", "coloring/books"), loaded.S3Storage);
        var persisted = await File.ReadAllTextAsync(paths.SettingsFile.Value);
        Assert.DoesNotContain("accessKey", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secretKey", persisted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_legacy_settings_default_generic_keywords_to_empty()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(paths.SettingsFile.Value, "{\"maximumPageConcurrency\":4,\"artworkDetectionThreshold\":20,\"artworkMaximumSide\":2270,\"workingPageWidth\":2550,\"workingPageHeight\":2550,\"finalPageWidth\":2588,\"finalPageHeight\":2625,\"dpi\":300}");

        var loaded = await CreateStore(paths).LoadAsync(paths);

        Assert.Empty(loaded.EffectiveGenericKeywords);
        Assert.Empty(loaded.GenericKeywords!);
    }

    [Theory]
    [InlineData(0, 320)]
    [InlineData(2048, 1024)]
    public async Task SaveAsync_rejects_invalid_detection_settings(int sourceSize, int pass2)
    {
        var paths = CreatePaths();
        var invalid = GlobalSettings.Default with
        {
            ArtworkSourceNormalization = new ArtworkSourceNormalizationSettings(sourceSize),
            BorderLineDetection = BorderLineDetectionSettings.Default with { Pass2SearchDepth = pass2 }
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateStore(paths).SaveAsync(invalid).AsTask());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
    }

    private ApplicationPaths CreatePaths() => new(new DirectoryReference(root), new DirectoryReference(Path.Combine(root, "brands")), new DirectoryReference(Path.Combine(root, "sources")), new FileReference(Path.Combine(root, "settings.json")));

    private static JsonGlobalSettingsStore CreateStore(ApplicationPaths paths) => new(new Discovery(paths), new PhysicalFileSystem());

    private sealed class Discovery(ApplicationPaths paths) : IApplicationRootDiscovery
    {
        public ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDiscovery(paths, [], []));
    }
}
