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
        var resultsDirectory = Path.Combine(corpusDirectory, "results");
        var debugDirectory = Path.Combine(resultsDirectory, "debug");
        Directory.CreateDirectory(resultsDirectory);
        Directory.CreateDirectory(debugDirectory);
        var detector = new MagickBorderLineDetector();
        var results = new List<BorderLineCorpusResult>();

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
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var measurement = await detector.MeasureAsync(new BorderLineDetectionRequest(
                        new FileReference(input), new ArtworkDetectionThreshold(20)));
                    var debugImage = WriteDebugOverlay(category.Name, input, debugDirectory, measurement.Detection);
                    stopwatch.Stop();
                    results.Add(BorderLineCorpusResult.Completed(
                        category.Name,
                        input,
                        category.ShouldHaveBorder,
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

        var reportPath = Path.Combine(resultsDirectory, "borderline-v2-measurement-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            threshold = 20,
            detectorVersion = "V2-segmented-outer-frame",
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
            BorderLineMeasurement measurement,
            string? debugImage,
            double elapsedMilliseconds) =>
            new(
                category,
                input,
                expectedHasBorder,
                measurement.Detection.HasBorder,
                measurement.Detection.HasBorder == expectedHasBorder ? "PASS" : "FAIL",
                measurement.Detection.Left,
                measurement.Detection.Right,
                measurement.Detection.Top,
                measurement.Detection.Bottom,
                measurement.Detection.BorderBounds,
                measurement,
                debugImage,
                elapsedMilliseconds,
                measurement.Detection.HasBorder == expectedHasBorder
                    ? null
                    : $"Expected HasBorder={expectedHasBorder}, actual HasBorder={measurement.Detection.HasBorder}.");

        public static BorderLineCorpusResult Failed(
            string category,
            string input,
            bool expectedHasBorder,
            string error,
            double elapsedMilliseconds) =>
            new(category, input, expectedHasBorder, null, "FAIL", null, null, null, null, null, null, null, elapsedMilliseconds, error);

        public static BorderLineCorpusResult ConfigurationFailure(
            string category,
            bool expectedHasBorder,
            string error) =>
            new(category, null, expectedHasBorder, null, "FAIL", null, null, null, null, null, null, null, null, error);
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
