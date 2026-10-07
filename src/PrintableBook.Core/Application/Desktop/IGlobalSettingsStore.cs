using System.Text.Json.Serialization;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Core.Application.AmazonCrawl;

namespace PrintableBook.Core.Application.Desktop;

public sealed record GlobalSettings(int MaximumPageConcurrency, byte ArtworkDetectionThreshold, int ArtworkMaximumSide, int WorkingPageWidth, int WorkingPageHeight, int FinalPageWidth, int FinalPageHeight, int Dpi, ArtworkSourceNormalizationSettings? ArtworkSourceNormalization = null, BorderLineDetectionSettings? BorderLineDetection = null, IReadOnlyList<string>? GenericKeywords = null, S3StorageConfiguration? S3Storage = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? GenericKeywordsByLanguage = null, IReadOnlyDictionary<string, AmazonMarketplaceSettings>? AmazonMarketplaceProfiles = null)
{
    public static GlobalSettings Default { get; } = new(4, 20, 2270, 2550, 2550, 2588, 2625, 300);

    /// <summary>
    /// The physical PDF dimensions of a final Interior page. They are derived
    /// from the final raster, never from the square working area.
    /// </summary>
    [JsonIgnore]
    public PhysicalPageSize FinalInteriorPdfPageSize => new(
        FinalPageWidth / (double)Dpi,
        FinalPageHeight / (double)Dpi);

    /// <summary>
    /// Cover page geometry remains independent of the Interior raster contract.
    /// </summary>
    public static PhysicalPageSize DefaultCoverPdfPageSize { get; } = new(8.5, 8.5);

    public ArtworkSourceNormalizationSettings EffectiveArtworkSourceNormalization =>
        ArtworkSourceNormalization ?? ArtworkSourceNormalizationSettings.Default;

    public BorderLineDetectionSettings EffectiveBorderLineDetection =>
        BorderLineDetection ?? BorderLineDetectionSettings.Default;

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveGenericKeywords => GenericKeywords ?? [];

    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectiveGenericKeywordsByLanguage =>
        SupportedLanguageCatalog.All.ToDictionary(
            language => language.Code,
            language => GetEffectiveGenericKeywords(language.Code),
            StringComparer.Ordinal);

    public IReadOnlyList<string> GetEffectiveGenericKeywords(string? languageCode)
    {
        var language = SupportedLanguageCatalog.GetEffective(languageCode);
        if (GenericKeywordsByLanguage is not null)
        {
            var profile = GenericKeywordsByLanguage.FirstOrDefault(
                pair => string.Equals(pair.Key, language.Code, StringComparison.OrdinalIgnoreCase));
            if (profile.Key is not null)
            {
                return profile.Value ?? [];
            }
        }

        return language.Code == "en" ? EffectiveGenericKeywords : [];
    }

    [JsonIgnore]
    public IReadOnlyDictionary<string, AmazonMarketplaceSettings> EffectiveAmazonMarketplaceProfiles =>
        AmazonMarketplaceProfilePolicy.NormalizeLoaded(this).AmazonMarketplaceProfiles!;

    [JsonIgnore]
    public S3StorageConfiguration EffectiveS3Storage => S3StoragePolicy.NormalizeConfiguration(S3Storage ?? S3StorageConfiguration.Default);
}

public interface IGlobalSettingsStore
{
    ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default);
}
