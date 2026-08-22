using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

/// <summary>
/// Executes user-supplied artwork from TestResults/InteriorProcessing/trim/custom.
/// It is deliberately a real-file corpus, not a mocked processor test.
/// </summary>
public sealed class CustomTrimCorpusTests
{
    [Fact]
    public async Task TrimAsync_processes_every_user_supplied_custom_image_and_writes_a_review_report()
    {
        var customDirectory = FindCustomDirectory();
        var outputDirectory = Path.Combine(customDirectory, "output");
        Directory.CreateDirectory(outputDirectory);
        var inputs = Directory.EnumerateFiles(customDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(outputDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(IsSupportedImage)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.NotEmpty(inputs);

        var processor = new MagickArtworkTrimProcessor();
        var inspector = new MagickImageInspector();
        var report = new List<CustomTrimResult>();
        foreach (var input in inputs)
        {
            var id = Path.GetFileNameWithoutExtension(input);
            var output = Path.Combine(outputDirectory, $"{id}.trim.png");
            try
            {
                var sourceInfo = await inspector.GetInfoAsync(new FileReference(input));
                var trimmed = await processor.TrimAsync(new ArtworkTrimRequest(
                    new FileReference(input), new FileReference(output), new ArtworkDetectionThreshold(20)));
                ImageSize? outputSize = trimmed.HasArtwork
                    ? (await inspector.GetInfoAsync(new FileReference(output))).Size
                    : null;
                report.Add(CustomTrimResult.Passed(id, input, output, sourceInfo.Size, trimmed.ArtworkBounds, outputSize));
            }
            catch (Exception exception)
            {
                report.Add(CustomTrimResult.Failed(id, input, output, exception.Message));
            }
        }

        var reportPath = Path.Combine(customDirectory, "report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            threshold = 20,
            sourceDirectory = customDirectory,
            outputDirectory,
            total = report.Count,
            passed = report.Count(result => result.Status == "PASS"),
            failed = report.Count(result => result.Status == "FAIL"),
            results = report
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.DoesNotContain(report, result => result.Status == "FAIL");
    }

    private static bool IsSupportedImage(string path) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string FindCustomDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var custom = Path.Combine(directory.FullName, "TestResults", "InteriorProcessing", "trim", "custom");
            if (Directory.Exists(custom)) return custom;
        }

        throw new DirectoryNotFoundException("Expected user images under TestResults/InteriorProcessing/trim/custom.");
    }

    private sealed record CustomTrimResult(
        string Id,
        string Input,
        string Output,
        string Status,
        ImageSize? InputSize,
        ImageRectangle? ArtworkBounds,
        ImageSize? OutputSize,
        string? Error)
    {
        public static CustomTrimResult Passed(string id, string input, string output, ImageSize inputSize, ImageRectangle? bounds, ImageSize? outputSize) =>
            new(id, input, output, "PASS", inputSize, bounds, outputSize, null);

        public static CustomTrimResult Failed(string id, string input, string output, string error) =>
            new(id, input, output, "FAIL", null, null, null, error);
    }
}
