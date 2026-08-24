using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Measures persistent outer-border tracks and selects the outermost coherent four-sided frame.
/// </summary>
public sealed class MagickBorderLineDetector : IBorderLineDetector
{
    private const int SearchDepth = 100;
    private const int SearchBandSize = SearchDepth + 1;
    private const int SegmentCount = 8;
    private const double CornerExclusionRatio = 0.10;
    private const int TrackDepthTolerance = 3;
    private const byte MinimumOpaqueAlpha = 128;

    private const double MinimumSegmentSupportRatio = 0.35;
    private const double MinimumSideSupportRatio = 0.55;
    private const double MinimumSpanRatio = 0.70;
    private const int MinimumSupportedSegments = 6;
    private const int MaximumDepthSpread = 12;
    private const int MaximumMissingSegmentRun = 2;

    public ValueTask<BorderLineDetectionResult> DetectAsync(
        BorderLineDetectionRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Measure(request, cancellationToken).Detection);

    /// <summary>
    /// Returns V2 diagnostic measurements without expanding the Core detector contract.
    /// </summary>
    public ValueTask<BorderLineMeasurement> MeasureAsync(
        BorderLineDetectionRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Measure(request, cancellationToken));

    private static BorderLineMeasurement Measure(
        BorderLineDetectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(request.Source.Value);
        using var pixels = image.GetPixels();
        var imageWidth = checked((int)image.Width);
        var imageHeight = checked((int)image.Height);

        var left = MeasureSide(pixels, BorderSide.Left, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);
        var right = MeasureSide(pixels, BorderSide.Right, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);
        var top = MeasureSide(pixels, BorderSide.Top, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);
        var bottom = MeasureSide(pixels, BorderSide.Bottom, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);

        var frameCandidates = BuildFrameCandidates(left, right, top, bottom, imageWidth, imageHeight);
        var selected = frameCandidates
            .Where(candidate => candidate.HasValidGeometry)
            .OrderBy(candidate => candidate.OuterDepthScore)
            .FirstOrDefault();
        var detection = selected is null
            ? BorderLineDetectionResult.NoBorder()
            : CreateDetection(selected, imageWidth, imageHeight);

        return new BorderLineMeasurement(
            new ImageSize(imageWidth, imageHeight),
            detection,
            left,
            right,
            top,
            bottom,
            frameCandidates,
            MeasureCorners(selected));
    }

    private static BorderTrackSideMeasurement MeasureSide(
        IPixelCollection<byte> pixels,
        BorderSide side,
        int imageWidth,
        int imageHeight,
        byte threshold,
        CancellationToken cancellationToken)
    {
        var corridor = CreateCorridor(side, imageWidth, imageHeight);
        cancellationToken.ThrowIfCancellationRequested();
        var rgba = pixels.ToByteArray(
            corridor.X,
            corridor.Y,
            (uint)corridor.Width,
            (uint)corridor.Height,
            PixelMapping.RGBA)
            ?? throw new InvalidDataException($"Unable to read the {side} outer border corridor.");
        var candidates = new List<BorderTrackSideCandidate>();

        for (var seedDepth = 0; seedDepth < corridor.DepthLength; seedDepth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = MeasureCandidate(rgba, corridor, seedDepth, threshold, cancellationToken);
            if (candidate.SupportRatio >= 0.10 && candidate.SupportedSegments >= 2)
            {
                candidates.Add(candidate);
            }
        }

        return new BorderTrackSideMeasurement(candidates
            .GroupBy(candidate => candidate.RepresentativeDepth)
            .Select(group => group
                .OrderByDescending(candidate => candidate.SupportRatio)
                .ThenByDescending(candidate => candidate.SupportedSegments)
                .First())
            .OrderBy(candidate => candidate.RepresentativeDepth)
            .ToArray());
    }

    private static BorderTrackSideCandidate MeasureCandidate(
        byte[] rgba,
        OuterCorridor corridor,
        int seedDepth,
        byte threshold,
        CancellationToken cancellationToken)
    {
        var segments = new List<BorderTrackSegmentEvidence>(corridor.SegmentCount);
        var allObservedDepths = new List<int>(corridor.ScanLength);
        var totalSupported = 0;
        var firstSupportedSegment = -1;
        var lastSupportedSegment = -1;
        var longestMissingRun = 0;
        var currentMissingRun = 0;

        for (var segmentIndex = 0; segmentIndex < corridor.SegmentCount; segmentIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localStart = (segmentIndex * corridor.ScanLength) / corridor.SegmentCount;
            var localEnd = (((segmentIndex + 1) * corridor.ScanLength) / corridor.SegmentCount) - 1;
            var observedDepths = new List<int>(localEnd - localStart + 1);

            for (var scanIndex = localStart; scanIndex <= localEnd; scanIndex++)
            {
                if (TryFindInkDepth(rgba, corridor, scanIndex, seedDepth, threshold, out var observedDepth))
                {
                    observedDepths.Add(observedDepth);
                }
            }

            var observedCount = localEnd - localStart + 1;
            var supportedCount = observedDepths.Count;
            var supportRatio = observedCount == 0 ? 0 : (double)supportedCount / observedCount;
            var isSupported = supportRatio >= MinimumSegmentSupportRatio;
            if (isSupported)
            {
                totalSupported += supportedCount;
                allObservedDepths.AddRange(observedDepths);
                firstSupportedSegment = firstSupportedSegment < 0 ? segmentIndex : firstSupportedSegment;
                lastSupportedSegment = segmentIndex;
                currentMissingRun = 0;
            }
            else
            {
                currentMissingRun++;
                longestMissingRun = Math.Max(longestMissingRun, currentMissingRun);
            }

            int? representativeDepth = observedDepths.Count == 0 ? null : Median(observedDepths);
            int? minDepth = observedDepths.Count == 0 ? null : observedDepths.Min();
            int? maxDepth = observedDepths.Count == 0 ? null : observedDepths.Max();
            segments.Add(new BorderTrackSegmentEvidence(
                segmentIndex,
                corridor.GlobalScanCoordinate(localStart),
                corridor.GlobalScanCoordinate(localEnd),
                observedCount,
                supportedCount,
                representativeDepth,
                minDepth,
                maxDepth,
                supportRatio,
                minDepth is null || maxDepth is null ? 0 : maxDepth.Value - minDepth.Value));
        }

        var representative = allObservedDepths.Count == 0 ? seedDepth : Median(allObservedDepths);
        var supportRatioForSide = corridor.ScanLength == 0 ? 0 : (double)totalSupported / corridor.ScanLength;
        var spanRatio = firstSupportedSegment < 0
            ? 0
            : (double)(segments[lastSupportedSegment].EndCoordinate - segments[firstSupportedSegment].StartCoordinate + 1) / corridor.ScanLength;
        var segmentDepths = segments
            .Where(segment => segment.RepresentativeDepth is not null && segment.SupportRatio >= MinimumSegmentSupportRatio)
            .Select(segment => segment.RepresentativeDepth!.Value)
            .ToArray();

        return new BorderTrackSideCandidate(
            representative,
            segments.Count(segment => segment.SupportRatio >= MinimumSegmentSupportRatio),
            corridor.SegmentCount,
            supportRatioForSide,
            spanRatio,
            segmentDepths.Length == 0 ? 0 : segmentDepths.Max() - segmentDepths.Min(),
            longestMissingRun,
            segments);
    }

    private static bool TryFindInkDepth(
        byte[] rgba,
        OuterCorridor corridor,
        int scanIndex,
        int seedDepth,
        byte threshold,
        out int observedDepth)
    {
        for (var distance = 0; distance <= TrackDepthTolerance; distance++)
        {
            var shallower = seedDepth - distance;
            if (shallower >= 0 && IsInkAtDepth(rgba, corridor, scanIndex, shallower, threshold))
            {
                observedDepth = shallower;
                return true;
            }

            var deeper = seedDepth + distance;
            if (distance > 0 && deeper < corridor.DepthLength && IsInkAtDepth(rgba, corridor, scanIndex, deeper, threshold))
            {
                observedDepth = deeper;
                return true;
            }
        }

        observedDepth = default;
        return false;
    }

    private static bool IsInkAtDepth(
        byte[] rgba,
        OuterCorridor corridor,
        int scanIndex,
        int depth,
        byte threshold)
    {
        var localDepth = corridor.ReversesDepth ? corridor.DepthLength - 1 - depth : depth;
        var pixelIndex = corridor.IsVertical
            ? ((scanIndex * corridor.DepthLength) + localDepth) * 4
            : ((localDepth * corridor.ScanLength) + scanIndex) * 4;
        return IsBlack(rgba, pixelIndex, threshold);
    }

    private static IReadOnlyList<BorderFrameCandidate> BuildFrameCandidates(
        BorderTrackSideMeasurement left,
        BorderTrackSideMeasurement right,
        BorderTrackSideMeasurement top,
        BorderTrackSideMeasurement bottom,
        int imageWidth,
        int imageHeight)
    {
        var leftCandidates = ValidCandidates(left).ToArray();
        var rightCandidates = ValidCandidates(right).ToArray();
        var topCandidates = ValidCandidates(top).ToArray();
        var bottomCandidates = ValidCandidates(bottom).ToArray();
        var frames = new List<BorderFrameCandidate>();

        foreach (var leftCandidate in leftCandidates)
        foreach (var rightCandidate in rightCandidates)
        foreach (var topCandidate in topCandidates)
        foreach (var bottomCandidate in bottomCandidates)
        {
            var validGeometry = leftCandidate.RepresentativeDepth + rightCandidate.RepresentativeDepth < imageWidth - 1 &&
                topCandidate.RepresentativeDepth + bottomCandidate.RepresentativeDepth < imageHeight - 1;
            frames.Add(new BorderFrameCandidate(
                leftCandidate,
                rightCandidate,
                topCandidate,
                bottomCandidate,
                leftCandidate.RepresentativeDepth + rightCandidate.RepresentativeDepth + topCandidate.RepresentativeDepth + bottomCandidate.RepresentativeDepth,
                validGeometry));
        }

        return frames
            .OrderBy(candidate => candidate.OuterDepthScore)
            .Take(81)
            .ToArray();
    }

    private static IEnumerable<BorderTrackSideCandidate> ValidCandidates(BorderTrackSideMeasurement side) =>
        side.Candidates
            .Where(candidate =>
                candidate.SupportedSegments >= MinimumSupportedSegments &&
                candidate.SupportRatio >= MinimumSideSupportRatio &&
                candidate.SpanRatio >= MinimumSpanRatio &&
                candidate.DepthSpread <= MaximumDepthSpread &&
                candidate.LongestMissingSegmentRun <= MaximumMissingSegmentRun)
            .OrderBy(candidate => candidate.RepresentativeDepth)
            .Take(3);

    private static BorderLineDetectionResult CreateDetection(
        BorderFrameCandidate frame,
        int imageWidth,
        int imageHeight)
    {
        var left = BorderLineSideResult.Detected(frame.Left.RepresentativeDepth);
        var right = BorderLineSideResult.Detected(imageWidth - 1 - frame.Right.RepresentativeDepth);
        var top = BorderLineSideResult.Detected(frame.Top.RepresentativeDepth);
        var bottom = BorderLineSideResult.Detected(imageHeight - 1 - frame.Bottom.RepresentativeDepth);
        var bounds = new ImageRectangle(
            new ImagePoint(left.Position!.Value, top.Position!.Value),
            new ImageSize(
                right.Position!.Value - left.Position.Value + 1,
                bottom.Position!.Value - top.Position.Value + 1));
        return BorderLineDetectionResult.Detected(left, right, top, bottom, bounds);
    }

    private static IReadOnlyList<BorderCornerEvidence> MeasureCorners(BorderFrameCandidate? frame)
    {
        if (frame is null)
        {
            return
            [
                new BorderCornerEvidence("TopLeft", false),
                new BorderCornerEvidence("TopRight", false),
                new BorderCornerEvidence("BottomLeft", false),
                new BorderCornerEvidence("BottomRight", false)
            ];
        }

        return
        [
            new BorderCornerEvidence("TopLeft", IsEndpointSupported(frame.Left, true) && IsEndpointSupported(frame.Top, true)),
            new BorderCornerEvidence("TopRight", IsEndpointSupported(frame.Right, true) && IsEndpointSupported(frame.Top, false)),
            new BorderCornerEvidence("BottomLeft", IsEndpointSupported(frame.Left, false) && IsEndpointSupported(frame.Bottom, true)),
            new BorderCornerEvidence("BottomRight", IsEndpointSupported(frame.Right, false) && IsEndpointSupported(frame.Bottom, false))
        ];
    }

    private static bool IsEndpointSupported(BorderTrackSideCandidate candidate, bool first) =>
        candidate.Segments[first ? 0 : ^1].SupportRatio >= MinimumSegmentSupportRatio;

    private static OuterCorridor CreateCorridor(BorderSide side, int imageWidth, int imageHeight)
    {
        var isVertical = side is BorderSide.Left or BorderSide.Right;
        var fullScanLength = isVertical ? imageHeight : imageWidth;
        var scanStart = Math.Min((int)(fullScanLength * CornerExclusionRatio), Math.Max(0, fullScanLength - 1));
        var scanEnd = Math.Max(scanStart, fullScanLength - 1 - scanStart);
        var scanLength = scanEnd - scanStart + 1;
        var segmentCount = Math.Min(SegmentCount, scanLength);
        var depthLength = Math.Min(isVertical ? imageWidth : imageHeight, SearchBandSize);

        return side switch
        {
            BorderSide.Left => new OuterCorridor(0, scanStart, depthLength, scanLength, true, false, segmentCount, scanStart),
            BorderSide.Right => new OuterCorridor(imageWidth - depthLength, scanStart, depthLength, scanLength, true, true, segmentCount, scanStart),
            BorderSide.Top => new OuterCorridor(scanStart, 0, scanLength, depthLength, false, false, segmentCount, scanStart),
            BorderSide.Bottom => new OuterCorridor(scanStart, imageHeight - depthLength, scanLength, depthLength, false, true, segmentCount, scanStart),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
        };
    }

    private static int Median(List<int> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static bool IsBlack(byte[] rgba, int offset, byte threshold) =>
        rgba[offset] <= threshold &&
        rgba[offset + 1] <= threshold &&
        rgba[offset + 2] <= threshold &&
        rgba[offset + 3] >= MinimumOpaqueAlpha;

    private enum BorderSide { Left, Right, Top, Bottom }

    private readonly record struct OuterCorridor(
        int X,
        int Y,
        int Width,
        int Height,
        bool IsVertical,
        bool ReversesDepth,
        int SegmentCount,
        int GlobalScanStart)
    {
        public int ScanLength => IsVertical ? Height : Width;

        public int DepthLength => IsVertical ? Width : Height;

        public int GlobalScanCoordinate(int localCoordinate) => GlobalScanStart + localCoordinate;
    }
}
