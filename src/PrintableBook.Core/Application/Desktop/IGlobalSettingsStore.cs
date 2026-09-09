using System.Text.Json.Serialization;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record GlobalSettings(int MaximumPageConcurrency, byte ArtworkDetectionThreshold, int ArtworkMaximumSide, int WorkingPageWidth, int WorkingPageHeight, int FinalPageWidth, int FinalPageHeight, int Dpi, ArtworkSourceNormalizationSettings? ArtworkSourceNormalization = null, BorderLineDetectionSettings? BorderLineDetection = null)
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
}

public interface IGlobalSettingsStore
{
    ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default);
}
