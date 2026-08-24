using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Diagnostic evidence produced by the V2 outer-frame detector. It remains an Infrastructure concern.
/// </summary>
public sealed record BorderLineMeasurement(
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
    bool HasValidGeometry);

/// <summary>
/// Bounded evidence observed near one prospective frame corner.
/// </summary>
public sealed record BorderCornerEvidence(string Corner, bool HasOuterInkEvidence);
