using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;

namespace PrintableBook.Infrastructure.Tests;

/// <summary>
/// Runs user-supplied artwork through the full classified Interior workflow. It is opt-in and excluded from CI.
/// </summary>
[Trait(InfrastructureTestScopes.TraitName, InfrastructureTestScopes.LocalCorpus)]
public sealed class InteriorWorkflowLocalCorpusTests
{
    private static readonly ImageSize PreparedSize = new(2270, 2270);
    private static readonly ImageSize WorkingSize = new(2550, 2550);
    private static readonly ImageSize FinalSize = new(2588, 2625);
    private static readonly CorpusCategory[] Categories =
    [
        new("borderart", ArtworkType.BorderArt, true),
        new("fullart", ArtworkType.FullArt, true),
        new("cropart", ArtworkType.CropArt, false)
    ];

    [LocalCorpusFact]
    public async Task ProcessAsync_certifies_real_full_interior_workflow()
    {
        var corpus = FindCorpusDirectory();
        var resultsDirectory = Path.Combine(corpus, "results");
        var frame = FindOptionalFrame(corpus);
        Directory.CreateDirectory(resultsDirectory);
        var pipeline = CreatePipeline();
        var inspector = new MagickImageInspector();
        var results = new List<WorkflowCorpusResult>();

        foreach (var category in Categories)
        {
            var categoryDirectory = Path.Combine(corpus, category.Name);
            if (!Directory.Exists(categoryDirectory))
            {
                results.Add(WorkflowCorpusResult.ConfigurationFailure(category.Name, category.ExpectedType, $"Expected corpus directory '{categoryDirectory}'."));
                continue;
            }

            var inputs = Directory.EnumerateFiles(categoryDirectory, "*", SearchOption.AllDirectories)
                .Where(IsSupportedImage)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (inputs.Length == 0)
            {
                results.Add(WorkflowCorpusResult.ConfigurationFailure(category.Name, category.ExpectedType, $"Expected at least one PNG, JPG, or JPEG under '{categoryDirectory}'."));
                continue;
            }

            for (var index = 0; index < inputs.Length; index++)
            {
                var input = inputs[index];
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var workspaceRoot = Path.Combine(resultsDirectory, "workspace", category.Name, $"{index + 1:D4}");
                    var workspace = new BookWorkspace(
                        new BookId($"local-{category.Name}-{index + 1:D4}"),
                        new DirectoryReference(Path.Combine(workspaceRoot, "working")),
                        new DirectoryReference(Path.Combine(workspaceRoot, "processed")),
                        new DirectoryReference(Path.Combine(workspaceRoot, "output-temp")));
                    var result = await pipeline.ProcessAsync(new InteriorPagePipelineRequest(
                        workspace,
                        new FileReference(input),
                        "page-01",
                        new ArtworkDetectionThreshold(20),
                        PreparedSize,
                        WorkingSize,
                        FinalSize,
                        new ImageDensity(300, 300),
                        frame,
                        FrameMode.Auto));
                    stopwatch.Stop();

                    var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
                    using var classification = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache, "classification.json")));
                    var actualType = ReadCanonicalArtworkType(classification.RootElement.GetProperty("Type").GetString());
                    var outputPaths = CopyArtifacts(categoryDirectory, resultsDirectory, category.Name, input, cache, result.FinalPage.Value);
                    var prepared = await inspector.GetInfoAsync(new FileReference(outputPaths.Prepared));
                    var framed = await inspector.GetInfoAsync(new FileReference(outputPaths.Framed));
                    var working = await inspector.GetInfoAsync(new FileReference(outputPaths.Working));
                    var final = await inspector.GetInfoAsync(new FileReference(outputPaths.Final));
                    var opaque = IsOpaque(outputPaths.Prepared);
                    var framedDiffersFromPrepared = !HashesMatch(outputPaths.Prepared, outputPaths.Framed);
                    var actualAutoFrameRecommended = actualType != ArtworkType.CropArt;
                    var frameMode = FrameMode.Auto;
                    var frameApplied = frame is not null && frameMode == FrameMode.Auto && actualAutoFrameRecommended && framedDiffersFromPrepared;
                    results.Add(WorkflowCorpusResult.Completed(
                        category.Name,
                        input,
                        category.ExpectedType,
                        actualType,
                        category.ExpectedAutoFrameRecommended,
                        actualAutoFrameRecommended,
                        frameMode,
                        frame is not null,
                        frameApplied,
                        framedDiffersFromPrepared,
                        prepared.Size,
                        framed.Size,
                        working.Size,
                        final.Size,
                        opaque,
                        stopwatch.Elapsed.TotalMilliseconds));
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    results.Add(WorkflowCorpusResult.Failed(category.Name, input, category.ExpectedType, category.ExpectedAutoFrameRecommended, exception.Message, stopwatch.Elapsed.TotalMilliseconds));
                }
            }
        }

        var reportPath = Path.Combine(resultsDirectory, "interior-workflow-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            classificationVersion = ClassificationAlgorithmVersion.Current,
            preparationVersion = ArtworkPreparationAlgorithmVersion.Current,
            frame = frame?.Value,
            preparedArtworkSize = PreparedSize,
            workingPageSize = WorkingSize,
            finalPageSize = FinalSize,
            total = results.Count,
            passed = results.Count(item => item.Status == "PASS"),
            failed = results.Count(item => item.Status != "PASS"),
            results = results.Select(result => new
            {
                category = result.Category,
                input = result.Input,
                expectedType = ToCanonicalArtworkType(result.ExpectedType),
                actualType = result.ActualType is { } actualType ? ToCanonicalArtworkType(actualType) : null,
                expectedAutoFrameRecommended = result.ExpectedAutoFrameRecommended,
                autoFrameRecommended = result.ActualAutoFrameRecommended,
                frameMode = result.FrameMode?.ToString().ToLowerInvariant(),
                frameAvailable = result.FrameAvailable,
                frameApplied = result.FrameApplied,
                framedDiffersFromPrepared = result.FramedDiffersFromPrepared,
                preparedSize = result.PreparedSize,
                framedSize = result.FramedSize,
                workingSize = result.WorkingSize,
                finalSize = result.FinalSize,
                isOpaque = result.IsOpaque,
                status = result.Status,
                elapsedMilliseconds = result.ElapsedMilliseconds,
                error = result.Error
            })
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, result => result.Status != "PASS");
    }

    private static DiskBackedInteriorPagePipeline CreatePipeline() => new(
        new ArtworkClassifier(new MagickBorderLineDetector(), new MagickBorderPixelDetector()),
        new ArtworkPreparationService(
            new BorderArtPreparationProcessor(new MagickBorderBoundsCropProcessor(), new MagickSquareCropProcessor(), new MagickArtworkResizeProcessor()),
            new FullArtPreparationProcessor(new MagickArtworkTrimProcessor(), new MagickSquareCropProcessor(), new MagickArtworkResizeProcessor()),
            new CropArtPreparationProcessor(new MagickArtworkTrimProcessor(), new MagickSquarePadProcessor(), new MagickArtworkResizeProcessor()),
            new MagickImageInspector()),
        new MagickFrameProcessor(),
        new MagickWorkingPageProcessor(),
        new MagickFinalInteriorPageProcessor(),
        new MagickImageInspector());

    private static (string Prepared, string Framed, string Working, string Final) CopyArtifacts(
        string categoryDirectory,
        string resultsDirectory,
        string category,
        string input,
        string cache,
        string finalPage)
    {
        var relative = Path.ChangeExtension(Path.GetRelativePath(categoryDirectory, input), ".png");
        var prepared = CopyArtifact(Path.Combine(cache, "prepared.png"), Path.Combine(resultsDirectory, "prepared", category, relative));
        var framed = CopyArtifact(Path.Combine(cache, "framed.png"), Path.Combine(resultsDirectory, "framed", category, relative));
        var working = CopyArtifact(Path.Combine(cache, "working-page.png"), Path.Combine(resultsDirectory, "working", category, relative));
        var final = CopyArtifact(finalPage, Path.Combine(resultsDirectory, "final", category, relative));
        return (prepared, framed, working, final);
    }

    private static string CopyArtifact(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    private static bool IsOpaque(string path)
    {
        using var image = new MagickImage(path);
        var rgba = image.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidDataException("Expected RGBA data from prepared artwork.");
        return rgba.Where((_, index) => index % 4 == 3).All(alpha => alpha == byte.MaxValue);
    }

    private static bool HashesMatch(string first, string second) =>
        SHA256.HashData(File.ReadAllBytes(first)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(second)));

    private static ArtworkType ReadCanonicalArtworkType(string? type) => type switch
    {
        "borderart" => ArtworkType.BorderArt,
        "fullart" => ArtworkType.FullArt,
        "cropart" => ArtworkType.CropArt,
        _ => throw new InvalidDataException("The classification cache did not contain a canonical artwork type.")
    };

    private static string ToCanonicalArtworkType(ArtworkType type) => type switch
    {
        ArtworkType.BorderArt => "borderart",
        ArtworkType.FullArt => "fullart",
        ArtworkType.CropArt => "cropart",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported artwork type.")
    };

    private static FileReference? FindOptionalFrame(string corpus) =>
        File.Exists(Path.Combine(corpus, "frame.png")) ? new FileReference(Path.Combine(corpus, "frame.png")) : null;

    private static bool IsSupportedImage(string path) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var corpus = Path.Combine(directory.FullName, "TestResults", "InteriorWorkflowCorpus");
            if (Directory.Exists(corpus)) return corpus;
        }

        throw new DirectoryNotFoundException("Expected user images under TestResults/InteriorWorkflowCorpus/{borderart,fullart,cropart}.");
    }

    private sealed record CorpusCategory(string Name, ArtworkType ExpectedType, bool ExpectedAutoFrameRecommended);

    private sealed record WorkflowCorpusResult(
        string Category,
        string? Input,
        ArtworkType ExpectedType,
        ArtworkType? ActualType,
        bool ExpectedAutoFrameRecommended,
        bool? ActualAutoFrameRecommended,
        FrameMode? FrameMode,
        bool? FrameAvailable,
        bool? FrameApplied,
        bool? FramedDiffersFromPrepared,
        ImageSize? PreparedSize,
        ImageSize? FramedSize,
        ImageSize? WorkingSize,
        ImageSize? FinalSize,
        bool? IsOpaque,
        string Status,
        double? ElapsedMilliseconds,
        string? Error)
    {
        public static WorkflowCorpusResult Completed(
            string category,
            string input,
            ArtworkType expectedType,
            ArtworkType actualType,
            bool expectedAutoFrameRecommended,
            bool actualAutoFrameRecommended,
            FrameMode frameMode,
            bool frameAvailable,
            bool frameApplied,
            bool framedDiffersFromPrepared,
            ImageSize preparedSize,
            ImageSize framedSize,
            ImageSize workingSize,
            ImageSize finalSize,
            bool isOpaque,
            double elapsedMilliseconds)
        {
            var valid = expectedType == actualType &&
                expectedAutoFrameRecommended == actualAutoFrameRecommended &&
                frameApplied == (frameAvailable && frameMode == global::PrintableBook.Core.Application.Processing.FrameMode.Auto && actualAutoFrameRecommended) &&
                (actualAutoFrameRecommended || !framedDiffersFromPrepared) &&
                preparedSize == InteriorWorkflowLocalCorpusTests.PreparedSize &&
                framedSize == InteriorWorkflowLocalCorpusTests.PreparedSize &&
                workingSize == InteriorWorkflowLocalCorpusTests.WorkingSize &&
                finalSize == InteriorWorkflowLocalCorpusTests.FinalSize &&
                isOpaque;
            var error = valid ? null : $"Expected Type={expectedType}, autoFrameRecommended={expectedAutoFrameRecommended}, prepared={InteriorWorkflowLocalCorpusTests.PreparedSize}, working={InteriorWorkflowLocalCorpusTests.WorkingSize}, final={InteriorWorkflowLocalCorpusTests.FinalSize}, opaque=true; actual Type={actualType}, autoFrameRecommended={actualAutoFrameRecommended}, frameAvailable={frameAvailable}, frameApplied={frameApplied}, framedDiffersFromPrepared={framedDiffersFromPrepared}, prepared={preparedSize}, working={workingSize}, final={finalSize}, opaque={isOpaque}.";
            return new(category, input, expectedType, actualType, expectedAutoFrameRecommended, actualAutoFrameRecommended, frameMode, frameAvailable, frameApplied, framedDiffersFromPrepared, preparedSize, framedSize, workingSize, finalSize, isOpaque, valid ? "PASS" : "FAIL", elapsedMilliseconds, error);
        }

        public static WorkflowCorpusResult Failed(string category, string input, ArtworkType expectedType, bool expectedAutoFrameRecommended, string error, double elapsedMilliseconds) =>
            new(category, input, expectedType, null, expectedAutoFrameRecommended, null, null, null, null, null, null, null, null, null, null, "FAIL", elapsedMilliseconds, error);

        public static WorkflowCorpusResult ConfigurationFailure(string category, ArtworkType expectedType, string error) =>
            new(category, null, expectedType, null, expectedType != ArtworkType.CropArt, null, null, null, null, null, null, null, null, null, null, "FAIL", null, error);
    }
}
