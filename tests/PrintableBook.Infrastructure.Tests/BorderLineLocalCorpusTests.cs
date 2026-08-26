using System.Diagnostics;
using System.Text.Json;
using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

/// <summary>
/// Reviews user-supplied real artwork in TestResults/BorderLineCorpus.
/// This test is intentionally opt-in and is never part of normal CI execution.
/// </summary>
[Trait(InfrastructureTestScopes.TraitName, InfrastructureTestScopes.LocalCorpus)]
public sealed class BorderLineLocalCorpusTests
{
    private static readonly CorpusCategory[] Categories =
    [
        new("borderart", true),
        new("fullart", false),
        new("cropart", false)
    ];

    [LocalCorpusFact]
    public async Task DetectAsync_reviews_every_user_supplied_borderline_corpus_image_and_writes_a_report()
    {
        var corpusDirectory = FindCorpusDirectory();
        var expectedBorderFrames = LoadExpectedBorderFrames(corpusDirectory);
        var resultsDirectory = Path.Combine(corpusDirectory, "results");
        var debugDirectory = Path.Combine(resultsDirectory, "debug");
        var normalizedDirectory = Path.Combine(resultsDirectory, "normalized");
        Directory.CreateDirectory(resultsDirectory);
        Directory.CreateDirectory(debugDirectory);
        Directory.CreateDirectory(normalizedDirectory);
        var detector = new MagickBorderLineDetector();
        var normalizer = new MagickArtworkSourceNormalizer();
        var results = new List<BorderLineCorpusResult>();
        var reviewedInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in Categories)
        {
            var categoryDirectory = Path.Combine(corpusDirectory, category.Name);
            if (!Directory.Exists(categoryDirectory))
            {
                results.Add(BorderLineCorpusResult.ConfigurationFailure(
                    category.Name,
                    category.ShouldHaveBorder,
                    $"Expected corpus directory '{categoryDirectory}'."));
                continue;
            }

            var inputs = Directory.EnumerateFiles(categoryDirectory, "*", SearchOption.AllDirectories)
                .Where(IsSupportedImage)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (inputs.Length == 0)
            {
                results.Add(BorderLineCorpusResult.ConfigurationFailure(
                    category.Name,
                    category.ShouldHaveBorder,
                    $"Expected at least one PNG, JPG, or JPEG under '{categoryDirectory}'."));
                continue;
            }

            foreach (var input in inputs)
            {
                var relativeInput = GetCorpusRelativePath(corpusDirectory, input);
                ImageRectangle? expectedBorderBounds = null;
                if (category.ShouldHaveBorder)
                {
                    reviewedInputs.Add(relativeInput);
                    if (!expectedBorderFrames.TryGetValue(relativeInput, out var reviewedFrame))
                    {
                        results.Add(BorderLineCorpusResult.ConfigurationFailure(
                            category.Name,
                            category.ShouldHaveBorder,
                            $"Expected reviewed outer-frame geometry for '{relativeInput}' in '{ExpectedFramesFileName}'."));
                        continue;
                    }

                    using var raw = new MagickImage(input);
                    expectedBorderBounds = reviewedFrame.ToBorderBounds(raw.Width, raw.Height, 2048);
                }

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var canonical = Path.Combine(normalizedDirectory, $"{Path.GetFileNameWithoutExtension(input)}.{Guid.NewGuid():N}.png");
                    await normalizer.NormalizeAsync(new ArtworkSourceNormalizationRequest(
                        new FileReference(input), new FileReference(canonical), new ImageSize(2048, 2048)));
                    var measurement = await detector.MeasureAsync(new BorderLineDetectionRequest(
                        new FileReference(canonical), new ArtworkDetectionThreshold(20), BorderLineDetectionSettings.Default));
                    var debugImage = WriteDebugOverlay(category.Name, canonical, debugDirectory, measurement.Detection);
                    stopwatch.Stop();
                    results.Add(BorderLineCorpusResult.Completed(
                        category.Name,
                        input,
                        category.ShouldHaveBorder,
                        expectedBorderBounds,
                        measurement,
                        debugImage,
                        stopwatch.Elapsed.TotalMilliseconds));
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    results.Add(BorderLineCorpusResult.Failed(
                        category.Name,
                        input,
                        category.ShouldHaveBorder,
                        exception.Message,
                        stopwatch.Elapsed.TotalMilliseconds));
                }
            }
        }

        foreach (var relativeInput in expectedBorderFrames.Keys.Except(reviewedInputs, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(BorderLineCorpusResult.ConfigurationFailure(
                "borderart",
                true,
                $"Reviewed outer-frame geometry exists for '{relativeInput}', but no matching borderart input was found."));
        }

        var reportPath = Path.Combine(resultsDirectory, "borderline-v3-measurement-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            threshold = 20,
            normalizationVersion = ArtworkSourceNormalizationAlgorithmVersion.Current,
            detectorVersion = BorderLineAlgorithmVersion.Current,
            canonicalSize = 2048,
            settings = BorderLineDetectionSettings.Default,
            corpusDirectory,
            total = results.Count,
            passed = results.Count(result => result.Status == "PASS"),
            failed = results.Count(result => result.Status == "FAIL"),
            results
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, result => result.Status == "FAIL");
    }

    private static bool IsSupportedImage(string path) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private const string ExpectedFramesFileName = "expected-outer-frames.json";

    private static IReadOnlyDictionary<string, ReviewedOuterFrame> LoadExpectedBorderFrames(string corpusDirectory)
    {
        var path = Path.Combine(corpusDirectory, ExpectedFramesFileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, ReviewedOuterFrame>(StringComparer.OrdinalIgnoreCase);
        }

        var frames = JsonSerializer.Deserialize<Dictionary<string, ReviewedOuterFrame>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return frames is null
            ? new Dictionary<string, ReviewedOuterFrame>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ReviewedOuterFrame>(frames, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetCorpusRelativePath(string corpusDirectory, string input) =>
        Path.GetRelativePath(corpusDirectory, input).Replace('\\', '/');

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var corpus = Path.Combine(directory.FullName, "TestResults", "BorderLineCorpus");
            if (Directory.Exists(corpus))
            {
                return corpus;
            }
        }

        throw new DirectoryNotFoundException(
            "Expected user images under TestResults/BorderLineCorpus/{borderart,fullart,cropart}.");
    }

    private sealed record CorpusCategory(string Name, bool ShouldHaveBorder);

    private sealed record BorderLineCorpusResult(
        string Category,
        string? Input,
        bool ExpectedHasBorder,
        ImageRectangle? ExpectedBorderBounds,
        bool? ActualHasBorder,
        string Status,
        BorderLineSideResult? Left,
        BorderLineSideResult? Right,
        BorderLineSideResult? Top,
        BorderLineSideResult? Bottom,
        ImageRectangle? BorderBounds,
        BorderLineMeasurement? Measurement,
        string? DebugImage,
        double? ElapsedMilliseconds,
        string? Error)
    {
        public static BorderLineCorpusResult Completed(
            string category,
            string input,
            bool expectedHasBorder,
            ImageRectangle? expectedBorderBounds,
            BorderLineMeasurement measurement,
            string? debugImage,
            double elapsedMilliseconds) =>
            new(
                category,
                input,
                expectedHasBorder,
                expectedBorderBounds,
                measurement.Detection.HasBorder,
                MatchesExpectation(measurement.Detection, expectedHasBorder, expectedBorderBounds) ? "PASS" : "FAIL",
                measurement.Detection.Left,
                measurement.Detection.Right,
                measurement.Detection.Top,
                measurement.Detection.Bottom,
                measurement.Detection.BorderBounds,
                measurement,
                debugImage,
                elapsedMilliseconds,
                DescribeMismatch(measurement.Detection, expectedHasBorder, expectedBorderBounds));

        public static BorderLineCorpusResult Failed(
            string category,
            string input,
            bool expectedHasBorder,
            string error,
            double elapsedMilliseconds) =>
            new(category, input, expectedHasBorder, null, null, "FAIL", null, null, null, null, null, null, null, elapsedMilliseconds, error);

        public static BorderLineCorpusResult ConfigurationFailure(
            string category,
            bool expectedHasBorder,
            string error) =>
            new(category, null, expectedHasBorder, null, null, "FAIL", null, null, null, null, null, null, null, null, error);

        private static bool MatchesExpectation(
            BorderLineDetectionResult detection,
            bool expectedHasBorder,
            ImageRectangle? expectedBorderBounds) =>
            detection.HasBorder == expectedHasBorder &&
            (!expectedHasBorder || detection.BorderBounds == expectedBorderBounds);

        private static string? DescribeMismatch(
            BorderLineDetectionResult detection,
            bool expectedHasBorder,
            ImageRectangle? expectedBorderBounds)
        {
            if (detection.HasBorder != expectedHasBorder)
            {
                return $"Expected HasBorder={expectedHasBorder}, actual HasBorder={detection.HasBorder}.";
            }

            return expectedHasBorder
                ? $"Expected BorderBounds={expectedBorderBounds}, actual BorderBounds={detection.BorderBounds}."
                : null;
        }
    }

    private sealed record ReviewedOuterFrame(int Left, int Right, int Top, int Bottom)
    {
        public ImageRectangle ToBorderBounds(uint rawWidth, uint rawHeight, int canonicalSize)
        {
            var left = Scale(Left, rawWidth, canonicalSize);
            var right = Scale(Right, rawWidth, canonicalSize);
            var top = Scale(Top, rawHeight, canonicalSize);
            var bottom = Scale(Bottom, rawHeight, canonicalSize);
            return new(new ImagePoint(left, top), new ImageSize(right - left + 1, bottom - top + 1));
        }

        private static int Scale(int coordinate, uint rawSize, int canonicalSize) =>
            (int)Math.Round(coordinate * canonicalSize / (double)rawSize, MidpointRounding.AwayFromZero);
    }

    private static string? WriteDebugOverlay(
        string category,
        string input,
        string debugDirectory,
        BorderLineDetectionResult detection)
    {
        if (!detection.HasBorder || detection.BorderBounds is null)
        {
            return null;
        }

        var id = Path.GetFileNameWithoutExtension(input);
        var output = Path.Combine(debugDirectory, $"{category}-{id}.detected.png");
        using var image = new MagickImage(input);
        var pixels = image.GetPixels();
        var bounds = detection.BorderBounds.Value;
        var left = bounds.Origin.X;
        var right = left + bounds.Size.Width - 1;
        var top = bounds.Origin.Y;
        var bottom = top + bounds.Size.Height - 1;
        for (var x = left; x <= right; x++)
        {
            pixels.SetPixel(x, top, [255, 0, 255, 255]);
            pixels.SetPixel(x, bottom, [255, 0, 255, 255]);
        }

        for (var y = top; y <= bottom; y++)
        {
            pixels.SetPixel(left, y, [255, 0, 255, 255]);
            pixels.SetPixel(right, y, [255, 0, 255, 255]);
        }

        image.Write(output);
        return output;
    }
}
