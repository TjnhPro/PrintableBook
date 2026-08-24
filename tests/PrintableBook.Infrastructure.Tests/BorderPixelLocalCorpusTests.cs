using System.Diagnostics;
using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

/// <summary>
/// Certifies user-supplied non-border artwork locally. This test is opt-in and never part of normal CI.
/// </summary>
[Trait(InfrastructureTestScopes.TraitName, InfrastructureTestScopes.LocalCorpus)]
public sealed class BorderPixelLocalCorpusTests
{
    private static readonly CorpusCategory[] Categories =
    [
        new("fullart", true),
        new("cropart", false)
    ];

    [LocalCorpusFact]
    public async Task DetectAsync_certifies_real_fullart_and_cropart_perimeter_contact()
    {
        var corpusDirectory = FindCorpusDirectory();
        var resultsDirectory = Path.Combine(corpusDirectory, "results");
        Directory.CreateDirectory(resultsDirectory);
        var borderLineDetector = new MagickBorderLineDetector();
        var borderPixelDetector = new MagickBorderPixelDetector();
        var results = new List<BorderPixelCorpusResult>();

        foreach (var category in Categories)
        {
            var categoryDirectory = Path.Combine(corpusDirectory, category.Name);
            if (!Directory.Exists(categoryDirectory))
            {
                results.Add(BorderPixelCorpusResult.ConfigurationFailure(
                    category.Name,
                    category.ExpectedHasBorderPixel,
                    $"Expected corpus directory '{categoryDirectory}'."));
                continue;
            }

            var inputs = Directory.EnumerateFiles(categoryDirectory, "*", SearchOption.AllDirectories)
                .Where(IsSupportedImage)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (inputs.Length == 0)
            {
                results.Add(BorderPixelCorpusResult.ConfigurationFailure(
                    category.Name,
                    category.ExpectedHasBorderPixel,
                    $"Expected at least one PNG, JPG, or JPEG under '{categoryDirectory}'."));
                continue;
            }

            foreach (var input in inputs)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var request = new BorderPixelDetectionRequest(new FileReference(input), new ArtworkDetectionThreshold(20));
                    var borderLine = await borderLineDetector.DetectAsync(
                        new BorderLineDetectionRequest(request.Source, request.Threshold));
                    if (borderLine.HasBorder)
                    {
                        stopwatch.Stop();
                        results.Add(BorderPixelCorpusResult.PreconditionFailure(
                            category.Name,
                            input,
                            category.ExpectedHasBorderPixel,
                            stopwatch.Elapsed.TotalMilliseconds));
                        continue;
                    }

                    var borderPixel = await borderPixelDetector.DetectAsync(request);
                    stopwatch.Stop();
                    results.Add(BorderPixelCorpusResult.Completed(
                        category.Name,
                        input,
                        category.ExpectedHasBorderPixel,
                        borderPixel,
                        stopwatch.Elapsed.TotalMilliseconds));
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    results.Add(BorderPixelCorpusResult.Failed(
                        category.Name,
                        input,
                        category.ExpectedHasBorderPixel,
                        exception.Message,
                        stopwatch.Elapsed.TotalMilliseconds));
                }
            }
        }

        var reportPath = Path.Combine(resultsDirectory, "borderpixel-v1-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            threshold = 20,
            detectorVersion = "borderpixel-v1-exact-perimeter",
            corpusDirectory,
            total = results.Count,
            passed = results.Count(result => result.Status == "PASS"),
            failed = results.Count(result => result.Status != "PASS"),
            results
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, result => result.Status != "PASS");
    }

    private static bool IsSupportedImage(string path) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var corpus = Path.Combine(directory.FullName, "TestResults", "BorderPixelCorpus");
            if (Directory.Exists(corpus))
            {
                return corpus;
            }
        }

        throw new DirectoryNotFoundException(
            "Expected user images under TestResults/BorderPixelCorpus/{fullart,cropart}.");
    }

    private sealed record CorpusCategory(string Name, bool ExpectedHasBorderPixel);

    private sealed record BorderPixelCorpusResult(
        string Category,
        string? Input,
        bool ExpectedHasBorderPixel,
        bool? BorderLineHasBorder,
        bool? ActualHasBorderPixel,
        bool? LeftHit,
        bool? RightHit,
        bool? TopHit,
        bool? BottomHit,
        string Status,
        double? ElapsedMilliseconds,
        string? Error)
    {
        public static BorderPixelCorpusResult Completed(
            string category,
            string input,
            bool expectedHasBorderPixel,
            BorderPixelDetectionResult result,
            double elapsedMilliseconds) =>
            new(
                category,
                input,
                expectedHasBorderPixel,
                false,
                result.HasBorderPixel,
                result.LeftHit,
                result.RightHit,
                result.TopHit,
                result.BottomHit,
                result.HasBorderPixel == expectedHasBorderPixel ? "PASS" : "FAIL",
                elapsedMilliseconds,
                result.HasBorderPixel == expectedHasBorderPixel
                    ? null
                    : $"Expected HasBorderPixel={expectedHasBorderPixel}, actual HasBorderPixel={result.HasBorderPixel}.");

        public static BorderPixelCorpusResult PreconditionFailure(
            string category,
            string input,
            bool expectedHasBorderPixel,
            double elapsedMilliseconds) =>
            new(category, input, expectedHasBorderPixel, true, null, null, null, null, null, "PRECONDITION_FAIL", elapsedMilliseconds,
                "BorderLine detected an outer frame, so BorderPixel must not evaluate this input as a fullart/cropart case.");

        public static BorderPixelCorpusResult Failed(
            string category,
            string input,
            bool expectedHasBorderPixel,
            string error,
            double elapsedMilliseconds) =>
            new(category, input, expectedHasBorderPixel, null, null, null, null, null, null, "FAIL", elapsedMilliseconds, error);

        public static BorderPixelCorpusResult ConfigurationFailure(
            string category,
            bool expectedHasBorderPixel,
            string error) =>
            new(category, null, expectedHasBorderPixel, null, null, null, null, null, null, "FAIL", null, error);
    }
}
