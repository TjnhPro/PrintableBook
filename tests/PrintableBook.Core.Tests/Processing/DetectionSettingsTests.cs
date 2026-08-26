using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class DetectionSettingsTests
{
    [Fact]
    public void Defaults_match_the_locked_canonical_processing_contract()
    {
        Assert.Equal(2048, ArtworkSourceNormalizationSettings.Default.NormalizedSourceSize);
        var settings = BorderLineDetectionSettings.Default;
        Assert.Equal((200, 320, 40, 6, 16, 24, 8, 3, 6, 2),
            (settings.Pass1SearchDepth, settings.Pass2SearchDepth, settings.CornerSearchPadding,
             settings.TrackDepthTolerance, settings.CornerLineTolerance, settings.MaximumDepthSpread,
             settings.SegmentCount, settings.MinimumCompatibleCorners, settings.MinimumSupportedSegments,
             settings.MaximumMissingSegmentRun));
        Assert.Equal((0.10, 0.35, 0.55, 0.70),
            (settings.CornerExclusionRatio, settings.MinimumSegmentSupportRatio,
             settings.MinimumSideSupportRatio, settings.MinimumSpanRatio));
    }

    [Fact]
    public void Legacy_global_settings_resolve_the_new_groups_to_defaults()
    {
        var settings = GlobalSettings.Default with { ArtworkSourceNormalization = null, BorderLineDetection = null };

        Assert.Same(ArtworkSourceNormalizationSettings.Default, settings.EffectiveArtworkSourceNormalization);
        Assert.Same(BorderLineDetectionSettings.Default, settings.EffectiveBorderLineDetection);
    }
}
