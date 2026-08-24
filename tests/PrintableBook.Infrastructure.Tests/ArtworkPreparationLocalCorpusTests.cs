using System.Diagnostics;
using System.Text.Json;
using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;

namespace PrintableBook.Infrastructure.Tests;

/// <summary>
/// Certifies real, user-supplied source artwork through the locked classification and preparation paths.
/// This test is opt-in and is never part of normal CI.
/// </summary>
[Trait(InfrastructureTestScopes.TraitName, InfrastructureTestScopes.LocalCorpus)]
public sealed class ArtworkPreparationLocalCorpusTests
{
    private const int PreparedArtworkSide = 2270;
    private static readonly CorpusCategory[] Categories =
    [
        new("borderart", ArtworkType.BorderArt, true),
        new("fullart", ArtworkType.FullArt, true),
        new("cropart", ArtworkType.CropArt, false)
    ];

    [LocalCorpusFact]
    public async Task PrepareAsync_certifies_real_classification_and_preparation()
    {
        var corpusDirectory = FindCorpusDirectory();
        var resultsDirectory = Path.Combine(corpusDirectory, "results");
        var preparedDirectory = Path.Combine(resultsDirectory, "prepared");
        Directory.CreateDirectory(preparedDirectory);

        var classifier = new ArtworkClassifier(new MagickBorderLineDetector(), new MagickBorderPixelDetector());
        var preparationService = CreatePreparationService();
        var results = new List<ArtworkPreparationCorpusResult>();

        foreach (var category in Categories)
        {
            var categoryDirectory = Path.Combine(corpusDirectory, category.Name);
            if (!Directory.Exists(categoryDirectory))
            {
                results.Add(ArtworkPreparationCorpusResult.ConfigurationFailure(
                    category.Name,
                    category.ExpectedType,
                    $"Expected corpus directory '{categoryDirectory}'."));
                continue;
            }

            var inputs = Directory.EnumerateFiles(categoryDirectory, "*", SearchOption.AllDirectories)
                .Where(IsSupportedImage)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (inputs.Length == 0)
            {
                results.Add(ArtworkPreparationCorpusResult.ConfigurationFailure(
                    category.Name,
                    category.ExpectedType,
                    $"Expected at least one PNG, JPG, or JPEG under '{categoryDirectory}'."));
                continue;
            }

            foreach (var input in inputs)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var classification = await classifier.ClassifyAsync(
                        new ArtworkClassificationRequest(new FileReference(input), new ArtworkDetectionThreshold(20)));
                    var output = CreateOutputPath(categoryDirectory, preparedDirectory, category.Name, input);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    var prepared = await preparationService.PrepareAsync(
                        new ArtworkPreparationRequest(
                            new FileReference(input),
                            new FileReference(output),
                            classification,
                            new ArtworkDetectionThreshold(20),
                            new ImageSize(PreparedArtworkSide, PreparedArtworkSide),
                            new ImageDensity(300, 300)));
                    stopwatch.Stop();

                    var inspection = InspectPreparedOutput(prepared.File.Value);
                    results.Add(ArtworkPreparationCorpusResult.Completed(
                        category.Name,
                        input,
                        category.ExpectedType,
                        category.ExpectedAutoFrameRecommended,
                        classification,
                        prepared,
                        inspection,
                        stopwatch.Elapsed.TotalMilliseconds));
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    results.Add(ArtworkPreparationCorpusResult.Failed(
                        category.Name,
                        input,
                        category.ExpectedType,
                        category.ExpectedAutoFrameRecommended,
                        exception.Message,
                        stopwatch.Elapsed.TotalMilliseconds));
                }
            }
        }

        var reportPath = Path.Combine(resultsDirectory, "artwork-preparation-v1-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            threshold = 20,
            preparedArtworkSize = new { width = PreparedArtworkSide, height = PreparedArtworkSide },
            classificationAlgorithmVersion = ClassificationAlgorithmVersion.Current,
            corpusDirectory,
            preparedDirectory,
            total = results.Count,
            passed = results.Count(result => result.Status == "PASS"),
            failed = results.Count(result => result.Status != "PASS"),
            results
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, result => result.Status != "PASS");
    }

    private static ArtworkPreparationService CreatePreparationService() => new(
        new BorderArtPreparationProcessor(
            new MagickBorderBoundsCropProcessor(),
            new MagickSquareCropProcessor(),
            new MagickArtworkResizeProcessor()),
        new FullArtPreparationProcessor(
            new MagickArtworkTrimProcessor(),
            new MagickSquareCropProcessor(),
            new MagickArtworkResizeProcessor()),
        new CropArtPreparationProcessor(
            new MagickArtworkTrimProcessor(),
            new MagickSquarePadProcessor(),
            new MagickArtworkResizeProcessor()),
        new MagickImageInspector());

    private static PreparedInspection InspectPreparedOutput(string output)
    {
        using var image = new MagickImage(output);
        var rgba = image.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidDataException("Expected RGBA pixel data from prepared artwork.");
        return new PreparedInspection(
            checked((int)image.Width),
            checked((int)image.Height),
            rgba.Where((_, index) => index % 4 == 3).All(alpha => alpha == byte.MaxValue));
    }

    private static string CreateOutputPath(string categoryDirectory, string preparedDirectory, string category, string input)
    {
        var relativeInput = Path.GetRelativePath(categoryDirectory, input);
        return Path.Combine(preparedDirectory, category, Path.ChangeExtension(relativeInput, ".prepared.png"));
    }

    private static bool IsSupportedImage(string path) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var corpus = Path.Combine(directory.FullName, "TestResults", "ArtworkPreparationCorpus");
            if (Directory.Exists(corpus))
            {
                return corpus;
            }
        }

        throw new DirectoryNotFoundException(
            "Expected user images under TestResults/ArtworkPreparationCorpus/{borderart,fullart,cropart}.");
    }

    private sealed record CorpusCategory(string Name, ArtworkType ExpectedType, bool ExpectedAutoFrameRecommended);

    private sealed record PreparedInspection(int Width, int Height, bool IsOpaque);

    private sealed record ArtworkPreparationCorpusResult(
        string Category,
        string? Input,
        ArtworkType ExpectedType,
        bool ExpectedAutoFrameRecommended,
        ArtworkType? ActualType,
        bool? ActualAutoFrameRecommended,
        int? PreparedWidth,
        int? PreparedHeight,
        bool? IsOpaque,
        string Status,
        double? ElapsedMilliseconds,
        string? Error)
    {
        public static ArtworkPreparationCorpusResult Completed(
            string category,
            string input,
            ArtworkType expectedType,
            bool expectedAutoFrameRecommended,
            ArtworkClassificationResult classification,
            PreparedArtwork prepared,
            PreparedInspection inspection,
            double elapsedMilliseconds)
        {
            var valid = classification.Type == expectedType &&
                prepared.AutoFrameRecommended == expectedAutoFrameRecommended &&
                inspection.Width == PreparedArtworkSide &&
                inspection.Height == PreparedArtworkSide &&
                inspection.IsOpaque;
            var error = valid
                ? null
                : $"Expected Type={expectedType}, AutoFrameRecommended={expectedAutoFrameRecommended}, {PreparedArtworkSide}x{PreparedArtworkSide}, opaque=true; " +
                  $"actual Type={classification.Type}, AutoFrameRecommended={prepared.AutoFrameRecommended}, {inspection.Width}x{inspection.Height}, opaque={inspection.IsOpaque}.";
            return new(
                category,
                input,
                expectedType,
                expectedAutoFrameRecommended,
                classification.Type,
                prepared.AutoFrameRecommended,
                inspection.Width,
                inspection.Height,
                inspection.IsOpaque,
                valid ? "PASS" : "FAIL",
                elapsedMilliseconds,
                error);
        }

        public static ArtworkPreparationCorpusResult Failed(
            string category,
            string input,
            ArtworkType expectedType,
            bool expectedAutoFrameRecommended,
            string error,
            double elapsedMilliseconds) =>
            new(category, input, expectedType, expectedAutoFrameRecommended, null, null, null, null, null, "FAIL", elapsedMilliseconds, error);

        public static ArtworkPreparationCorpusResult ConfigurationFailure(
            string category,
            ArtworkType expectedType,
            string error) =>
            new(category, null, expectedType, expectedType != ArtworkType.CropArt, null, null, null, null, null, "FAIL", null, error);
    }
}
