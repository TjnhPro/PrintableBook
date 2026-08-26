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
    public async Task LoadAsync_legacy_json_uses_and_materializes_detection_defaults()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(paths.SettingsFile.Value, "{\"maximumPageConcurrency\":4,\"artworkDetectionThreshold\":20,\"artworkMaximumSide\":2270,\"workingPageWidth\":2550,\"workingPageHeight\":2550,\"finalPageWidth\":2588,\"finalPageHeight\":2625,\"dpi\":300,\"interiorPdfWidthInches\":8.5,\"interiorPdfHeightInches\":8.5}");

        var loaded = await CreateStore(paths).LoadAsync(paths);

        Assert.Equal(2048, loaded.EffectiveArtworkSourceNormalization.NormalizedSourceSize);
        Assert.Equal(320, loaded.EffectiveBorderLineDetection.Pass2SearchDepth);
        Assert.NotNull(loaded.ArtworkSourceNormalization);
        Assert.NotNull(loaded.BorderLineDetection);
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
