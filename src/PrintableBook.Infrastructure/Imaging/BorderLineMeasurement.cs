using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Diagnostic evidence produced by the V3 two-pass outer-frame detector. It remains an Infrastructure concern.
/// </summary>
public sealed record BorderLineMeasurement(
    ImageSize ImageSize,
    BorderLineDetectionResult Detection,
    BorderLinePassMeasurement Pass1,
    BorderLinePassMeasurement? Pass2,
    int? SelectedPass)
{
    private BorderLinePassMeasurement Selected => SelectedPass == 2 ? Pass2! : Pass1;
    public BorderTrackSideMeasurement Left => Selected.Left;
    public BorderTrackSideMeasurement Right => Selected.Right;
    public BorderTrackSideMeasurement Top => Selected.Top;
    public BorderTrackSideMeasurement Bottom => Selected.Bottom;
    public IReadOnlyList<BorderFrameCandidate> FrameCandidates => Selected.FrameCandidates;
    public IReadOnlyList<BorderCornerEvidence> CornerEvidence => Selected.CornerEvidence;
}

public sealed record BorderLinePassMeasurement(
    int PassNumber,
    int SearchDepth,
    int CornerSearchSize,
    ImageSize ImageSize,
    BorderLineDetectionResult Detection,
    BorderTrackSideMeasurement Left,
    BorderTrackSideMeasurement Right,
    BorderTrackSideMeasurement Top,
    BorderTrackSideMeasurement Bottom,
    IReadOnlyList<BorderFrameCandidate> FrameCandidates,
    IReadOnlyList<BorderCornerEvidence> CornerEvidence);

/// <summary>
/// Candidate tracks measured on one outer image side.
/// </summary>
public sealed record BorderTrackSideMeasurement(
    IReadOnlyList<BorderTrackSideCandidate> Candidates);

/// <summary>
/// A shallow-depth track and its persistence measurements.
/// </summary>
public sealed record BorderTrackSideCandidate(
    int RepresentativeDepth,
    int SupportedSegments,
    int TotalSegments,
    double SupportRatio,
    double SpanRatio,
    int DepthSpread,
    int LongestMissingSegmentRun,
    IReadOnlyList<BorderTrackSegmentEvidence> Segments);

/// <summary>
/// Evidence for one portion of a side's usable sampling range.
/// </summary>
public sealed record BorderTrackSegmentEvidence(
    int SegmentIndex,
    int StartCoordinate,
    int EndCoordinate,
    int ObservedScanlines,
    int SupportedScanlines,
    int? RepresentativeDepth,
    int? MinDepth,
    int? MaxDepth,
    double SupportRatio,
    int DepthSpread);

/// <summary>
/// A possible four-sided frame formed from side candidates.
/// </summary>
public sealed record BorderFrameCandidate(
    BorderTrackSideCandidate Left,
    BorderTrackSideCandidate Right,
    BorderTrackSideCandidate Top,
    BorderTrackSideCandidate Bottom,
    int OuterDepthScore,
    bool HasValidGeometry,
    IReadOnlyList<BorderCornerEvidence> CornerEvidence,
    bool HasCornerCompatibility);

/// <summary>
/// Bounded evidence observed near one prospective frame corner.
/// </summary>
public sealed record BorderCornerEvidence(string Corner, bool HasOuterInkEvidence);
