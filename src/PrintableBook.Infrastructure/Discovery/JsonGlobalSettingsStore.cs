using System.Text.Json;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Abstractions;

namespace PrintableBook.Infrastructure.Discovery;

public sealed class JsonGlobalSettingsStore(IApplicationRootDiscovery discovery, IFileSystem fileSystem) : IGlobalSettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await LoadAsync((await discovery.DiscoverAsync(cancellationToken)).Paths, cancellationToken);
    }

    public async ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default)
    {
        if (!await fileSystem.FileExistsAsync(paths.SettingsFile, cancellationToken)) return Normalize(GlobalSettings.Default);
        var settings = JsonSerializer.Deserialize<GlobalSettings>(await fileSystem.ReadTextAsync(paths.SettingsFile, cancellationToken), Options) ?? GlobalSettings.Default;
        settings = Normalize(settings);
        Validate(settings);
        return settings;
    }

    public async ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        Validate(settings);
        var paths = (await discovery.DiscoverAsync(cancellationToken)).Paths;
        await fileSystem.WriteTextAtomicallyAsync(paths.SettingsFile, JsonSerializer.Serialize(settings, Options), cancellationToken);
    }

    private static void Validate(GlobalSettings value)
    {
        if (value.MaximumPageConcurrency is < 1 or > 12 || value.ArtworkMaximumSide <= 0 || value.WorkingPageWidth < value.ArtworkMaximumSide || value.WorkingPageHeight < value.ArtworkMaximumSide || value.FinalPageWidth < value.WorkingPageWidth || value.FinalPageHeight < value.WorkingPageHeight || value.Dpi <= 0 || value.InteriorPdfWidthInches <= 0 || value.InteriorPdfHeightInches <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Global settings contain an invalid processing layout.");

        var normalization = value.EffectiveArtworkSourceNormalization;
        var borderLine = value.EffectiveBorderLineDetection;
        if (normalization.NormalizedSourceSize <= 0 ||
            borderLine.Pass1SearchDepth <= 0 ||
            borderLine.Pass2SearchDepth < borderLine.Pass1SearchDepth ||
            borderLine.Pass2SearchDepth >= normalization.NormalizedSourceSize / 2 ||
            borderLine.CornerSearchPadding < 0 ||
            borderLine.Pass2SearchDepth + borderLine.CornerSearchPadding > normalization.NormalizedSourceSize / 2 ||
            borderLine.TrackDepthTolerance < 0 || borderLine.CornerLineTolerance < 0 ||
            borderLine.MaximumDepthSpread < 0 || borderLine.SegmentCount < 1 ||
            borderLine.MinimumCompatibleCorners is < 1 or > 4 ||
            !IsRatio(borderLine.CornerExclusionRatio) || !IsRatio(borderLine.MinimumSegmentSupportRatio) ||
            !IsRatio(borderLine.MinimumSideSupportRatio) || !IsRatio(borderLine.MinimumSpanRatio) ||
            borderLine.MinimumSupportedSegments < 1 || borderLine.MinimumSupportedSegments > borderLine.SegmentCount ||
            borderLine.MaximumMissingSegmentRun < 0 || borderLine.MaximumMissingSegmentRun >= borderLine.SegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Global settings contain invalid artwork detection values.");
        }
    }

    private static GlobalSettings Normalize(GlobalSettings settings) => settings with
    {
        ArtworkSourceNormalization = settings.EffectiveArtworkSourceNormalization,
        BorderLineDetection = settings.EffectiveBorderLineDetection
    };

    private static bool IsRatio(double value) => value is >= 0 and <= 1;
}
