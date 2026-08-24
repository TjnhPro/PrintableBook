using System.Text.Json;
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
        Directory.CreateDirectory(resultsDirectory);
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
                try
                {
                    var detected = await detector.DetectAsync(new BorderLineDetectionRequest(
                        new FileReference(input), new ArtworkDetectionThreshold(20)));
                    results.Add(BorderLineCorpusResult.Completed(
                        category.Name,
                        input,
                        category.ShouldHaveBorder,
                        detected));
                }
                catch (Exception exception)
                {
                    results.Add(BorderLineCorpusResult.Failed(
                        category.Name,
                        input,
                        category.ShouldHaveBorder,
                        exception.Message));
                }
            }
        }

        var reportPath = Path.Combine(resultsDirectory, "borderline-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            threshold = 20,
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
        string? Error)
    {
        public static BorderLineCorpusResult Completed(
            string category,
            string input,
            bool expectedHasBorder,
            BorderLineDetectionResult detected) =>
            new(
                category,
                input,
                expectedHasBorder,
                detected.HasBorder,
                detected.HasBorder == expectedHasBorder ? "PASS" : "FAIL",
                detected.Left,
                detected.Right,
                detected.Top,
                detected.Bottom,
                detected.BorderBounds,
                detected.HasBorder == expectedHasBorder
                    ? null
                    : $"Expected HasBorder={expectedHasBorder}, actual HasBorder={detected.HasBorder}.");

        public static BorderLineCorpusResult Failed(
            string category,
            string input,
            bool expectedHasBorder,
            string error) =>
            new(category, input, expectedHasBorder, null, "FAIL", null, null, null, null, null, error);

        public static BorderLineCorpusResult ConfigurationFailure(
            string category,
            bool expectedHasBorder,
            string error) =>
            new(category, null, expectedHasBorder, null, "FAIL", null, null, null, null, null, error);
    }
}
