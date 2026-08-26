using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>
/// Measures persistent outer-border tracks and selects the outermost coherent four-sided frame.
/// </summary>
public sealed class MagickBorderLineDetector : IBorderLineDetector
{
    [ThreadStatic] private static BorderLineDetectionSettings? activeSettings;
    [ThreadStatic] private static int activeSearchDepth;
    private static readonly BorderLineDetectionSettings LegacyStandaloneSettings = new(
        100, 100, 20, 3, 8, 12, 8, 0.10, 3, 0.35, 0.55, 0.70, 6, 2);
    private static BorderLineDetectionSettings Settings => activeSettings ?? BorderLineDetectionSettings.Default;
    private static int SearchDepth => activeSearchDepth == 0 ? Settings.Pass1SearchDepth : activeSearchDepth;
    private static int SearchBandSize => SearchDepth + 1;
    private static int SegmentCount => Settings.SegmentCount;
    private static double CornerExclusionRatio => Settings.CornerExclusionRatio;
    private static int CornerSearchSize => SearchDepth + Settings.CornerSearchPadding;
    private static int CornerLineTolerance => Settings.CornerLineTolerance;
    private static int MinimumCompatibleCorners => Settings.MinimumCompatibleCorners;
    private static int TrackDepthTolerance => Settings.TrackDepthTolerance;
    private const byte MinimumOpaqueAlpha = 128;

    private static double MinimumSegmentSupportRatio => Settings.MinimumSegmentSupportRatio;
    private static double MinimumSideSupportRatio => Settings.MinimumSideSupportRatio;
    private static double MinimumSpanRatio => Settings.MinimumSpanRatio;
    private static int MinimumSupportedSegments => Settings.MinimumSupportedSegments;
    private static int MaximumDepthSpread => Settings.MaximumDepthSpread;
    private static int MaximumMissingSegmentRun => Settings.MaximumMissingSegmentRun;

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

        var previousSettings = activeSettings;
        var previousDepth = activeSearchDepth;
        activeSettings = request.Settings ?? LegacyStandaloneSettings;
        try
        {
            activeSearchDepth = activeSettings.Pass1SearchDepth;
            var first = MeasurePass(request, cancellationToken);
            if (first.Detection.HasBorder) return first;
            activeSearchDepth = activeSettings.Pass2SearchDepth;
            return MeasurePass(request, cancellationToken);
        }
        finally
        {
            activeSettings = previousSettings;
            activeSearchDepth = previousDepth;
        }
    }

    private static BorderLineMeasurement MeasurePass(
        BorderLineDetectionRequest request,
        CancellationToken cancellationToken)
    {

        using var image = new MagickImage(request.Source.Value);
        using var pixels = image.GetPixels();
        var imageWidth = checked((int)image.Width);
        var imageHeight = checked((int)image.Height);

        var left = MeasureSide(pixels, BorderSide.Left, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);
        var right = MeasureSide(pixels, BorderSide.Right, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);
        var top = MeasureSide(pixels, BorderSide.Top, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);
        var bottom = MeasureSide(pixels, BorderSide.Bottom, imageWidth, imageHeight, request.Threshold.Value, cancellationToken);

        var cornerRegions = ReadCornerRegions(pixels, imageWidth, imageHeight, cancellationToken);
        var frameCandidates = BuildFrameCandidates(
            left,
            right,
            top,
            bottom,
            cornerRegions,
            imageWidth,
            imageHeight,
            request.Threshold.Value,
            cancellationToken);
        var selected = frameCandidates
            .Where(candidate => candidate.HasValidGeometry && candidate.HasCornerCompatibility)
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
            selected?.CornerEvidence ?? NoCornerEvidence());
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
        var rgba = ReadRgba(pixels, corridor.X, corridor.Y, corridor.Width, corridor.Height, side.ToString());
        var profile = BuildDepthProfile(rgba, corridor, threshold, cancellationToken);
        var candidates = new List<BorderTrackSideCandidate>();

        foreach (var seedDepth in profile.CandidateSeedDepths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = MeasureCandidate(profile, corridor, seedDepth, cancellationToken);
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
        DepthProfile profile,
        OuterCorridor corridor,
        int seedDepth,
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
                var observedDepth = profile.NearestDepth(scanIndex, seedDepth);
                if (observedDepth is not null)
                {
                    observedDepths.Add(observedDepth.Value);
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

    private static DepthProfile BuildDepthProfile(
        byte[] rgba,
        OuterCorridor corridor,
        byte threshold,
        CancellationToken cancellationToken)
    {
        var nearestDepthByScanlineAndSeed = Enumerable.Repeat(-1, corridor.ScanLength * corridor.DepthLength).ToArray();
        var histogram = new int[corridor.DepthLength];
        for (var scanIndex = 0; scanIndex < corridor.ScanLength; scanIndex++)
        {
            if (scanIndex % 128 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var inkDepth = 0; inkDepth < corridor.DepthLength; inkDepth++)
            {
                if (!IsInkAtDepth(rgba, corridor, scanIndex, inkDepth, threshold))
                {
                    continue;
                }

                histogram[inkDepth]++;
                var firstSeedDepth = Math.Max(0, inkDepth - TrackDepthTolerance);
                var lastSeedDepth = Math.Min(corridor.DepthLength - 1, inkDepth + TrackDepthTolerance);
                for (var seedDepth = firstSeedDepth; seedDepth <= lastSeedDepth; seedDepth++)
                {
                    var profileIndex = (scanIndex * corridor.DepthLength) + seedDepth;
                    var existingDepth = nearestDepthByScanlineAndSeed[profileIndex];
                    if (existingDepth < 0 || IsCloserToSeed(inkDepth, existingDepth, seedDepth))
                    {
                        nearestDepthByScanlineAndSeed[profileIndex] = inkDepth;
                    }
                }
            }
        }

        return new DepthProfile(
            nearestDepthByScanlineAndSeed,
            histogram
                .Select((count, depth) => (count, depth))
                .Where(entry => entry.count > 0)
                .Select(entry => entry.depth)
                .ToArray(),
            corridor.DepthLength);
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

    private static bool IsCloserToSeed(int candidateDepth, int existingDepth, int seedDepth) =>
        Math.Abs(candidateDepth - seedDepth) < Math.Abs(existingDepth - seedDepth) ||
        (Math.Abs(candidateDepth - seedDepth) == Math.Abs(existingDepth - seedDepth) && candidateDepth < existingDepth);

    private static IReadOnlyList<BorderFrameCandidate> BuildFrameCandidates(
        BorderTrackSideMeasurement left,
        BorderTrackSideMeasurement right,
        BorderTrackSideMeasurement top,
        BorderTrackSideMeasurement bottom,
        IReadOnlyList<CornerRegion> cornerRegions,
        int imageWidth,
        int imageHeight,
        byte threshold,
        CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            var validGeometry = leftCandidate.RepresentativeDepth + rightCandidate.RepresentativeDepth < imageWidth - 1 &&
                topCandidate.RepresentativeDepth + bottomCandidate.RepresentativeDepth < imageHeight - 1;
            var cornerEvidence = MeasureCornerEvidence(
                cornerRegions,
                leftCandidate.RepresentativeDepth,
                imageWidth - 1 - rightCandidate.RepresentativeDepth,
                topCandidate.RepresentativeDepth,
                imageHeight - 1 - bottomCandidate.RepresentativeDepth,
                threshold);
            frames.Add(new BorderFrameCandidate(
                leftCandidate,
                rightCandidate,
                topCandidate,
                bottomCandidate,
                leftCandidate.RepresentativeDepth + rightCandidate.RepresentativeDepth + topCandidate.RepresentativeDepth + bottomCandidate.RepresentativeDepth,
                validGeometry,
                cornerEvidence,
                cornerEvidence.Count(evidence => evidence.HasOuterInkEvidence) >= MinimumCompatibleCorners));
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

    private static IReadOnlyList<CornerRegion> ReadCornerRegions(
        IPixelCollection<byte> pixels,
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken)
    {
        var width = Math.Min(CornerSearchSize, imageWidth);
        var height = Math.Min(CornerSearchSize, imageHeight);
        return
        [
            ReadCornerRegion(pixels, "TopLeft", 0, 0, width, height, cancellationToken),
            ReadCornerRegion(pixels, "TopRight", imageWidth - width, 0, width, height, cancellationToken),
            ReadCornerRegion(pixels, "BottomLeft", 0, imageHeight - height, width, height, cancellationToken),
            ReadCornerRegion(pixels, "BottomRight", imageWidth - width, imageHeight - height, width, height, cancellationToken)
        ];
    }

    private static CornerRegion ReadCornerRegion(
        IPixelCollection<byte> pixels,
        string name,
        int x,
        int y,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new CornerRegion(name, x, y, width, height, ReadRgba(pixels, x, y, width, height, $"{name} corner"));
    }

    private static IReadOnlyList<BorderCornerEvidence> MeasureCornerEvidence(
        IReadOnlyList<CornerRegion> regions,
        int left,
        int right,
        int top,
        int bottom,
        byte threshold) =>
    [
        MeasureCorner(regions[0], left, top, threshold),
        MeasureCorner(regions[1], right, top, threshold),
        MeasureCorner(regions[2], left, bottom, threshold),
        MeasureCorner(regions[3], right, bottom, threshold)
    ];

    private static BorderCornerEvidence MeasureCorner(
        CornerRegion region,
        int expectedVerticalPosition,
        int expectedHorizontalPosition,
        byte threshold) =>
        new(
            region.Name,
            HasInkNearVerticalTrack(region, expectedVerticalPosition, threshold) &&
            HasInkNearHorizontalTrack(region, expectedHorizontalPosition, threshold));

    private static bool HasInkNearVerticalTrack(CornerRegion region, int expectedPosition, byte threshold)
    {
        var localStart = Math.Max(0, expectedPosition - region.X - CornerLineTolerance);
        var localEnd = Math.Min(region.Width - 1, expectedPosition - region.X + CornerLineTolerance);
        for (var localY = 0; localY < region.Height; localY++)
        for (var localX = localStart; localX <= localEnd; localX++)
        {
            if (IsBlack(region.Rgba, ((localY * region.Width) + localX) * 4, threshold))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInkNearHorizontalTrack(CornerRegion region, int expectedPosition, byte threshold)
    {
        var localStart = Math.Max(0, expectedPosition - region.Y - CornerLineTolerance);
        var localEnd = Math.Min(region.Height - 1, expectedPosition - region.Y + CornerLineTolerance);
        for (var localY = localStart; localY <= localEnd; localY++)
        for (var localX = 0; localX < region.Width; localX++)
        {
            if (IsBlack(region.Rgba, ((localY * region.Width) + localX) * 4, threshold))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<BorderCornerEvidence> NoCornerEvidence() =>
    [
        new BorderCornerEvidence("TopLeft", false),
        new BorderCornerEvidence("TopRight", false),
        new BorderCornerEvidence("BottomLeft", false),
        new BorderCornerEvidence("BottomRight", false)
    ];

    private static byte[] ReadRgba(
        IPixelCollection<byte> pixels,
        int x,
        int y,
        int width,
        int height,
        string description) =>
        pixels.ToByteArray(x, y, (uint)width, (uint)height, PixelMapping.RGBA)
        ?? throw new InvalidDataException($"Unable to read the {description} bounded raster region.");

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

    private readonly record struct DepthProfile(
        int[] NearestDepthByScanlineAndSeed,
        IReadOnlyList<int> CandidateSeedDepths,
        int DepthLength)
    {
        public int? NearestDepth(int scanIndex, int seedDepth)
        {
            var depth = NearestDepthByScanlineAndSeed[(scanIndex * DepthLength) + seedDepth];
            return depth < 0 ? null : depth;
        }
    }

    private readonly record struct CornerRegion(string Name, int X, int Y, int Width, int Height, byte[] Rgba);
}
