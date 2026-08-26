namespace PrintableBook.Core.Application.Processing;

/// <summary>Configuration for the two-pass outer-frame detector operating on canonical artwork.</summary>
public sealed record BorderLineDetectionSettings(
    int Pass1SearchDepth,
    int Pass2SearchDepth,
    int CornerSearchPadding,
    int TrackDepthTolerance,
    int CornerLineTolerance,
    int MaximumDepthSpread,
    int SegmentCount,
    double CornerExclusionRatio,
    int MinimumCompatibleCorners,
    double MinimumSegmentSupportRatio,
    double MinimumSideSupportRatio,
    double MinimumSpanRatio,
    int MinimumSupportedSegments,
    int MaximumMissingSegmentRun)
{
    public static BorderLineDetectionSettings Default { get; } = new(
        200, 320, 40, 6, 16, 24, 8, 0.10, 3, 0.35, 0.55, 0.70, 6, 2);
}
