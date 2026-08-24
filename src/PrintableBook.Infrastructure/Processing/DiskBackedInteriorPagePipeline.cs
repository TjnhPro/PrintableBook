using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Processing;

/// <summary>
/// Orchestrates the classified interior workflow through disk-backed, independently readable stages.
/// </summary>
public sealed class DiskBackedInteriorPagePipeline(
    IArtworkClassifier artworkClassifier,
    IArtworkPreparationService artworkPreparationService,
    IFrameProcessor frameProcessor,
    IWorkingPageProcessor workingPageProcessor,
    IFinalInteriorPageProcessor finalPageProcessor,
    IImageInspector imageInspector) : IInteriorPagePipeline
{
    private const string CacheStampSchemaVersion = "interior-page-cache-v1";

    public async ValueTask<InteriorPageProcessingResult> ProcessAsync(
        InteriorPagePipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PageId))
        {
            throw new ArgumentException("Page identity is required.", nameof(request));
        }

        request.ValidateGeometry();
        var pageCache = Path.Combine(request.Workspace.WorkingDirectory.Value, "cache", request.PageId);
        var processedInteriorDirectory = Path.Combine(request.Workspace.ProcessedDirectory.Value, "interior");
        var classificationFile = Path.Combine(pageCache, "classification.json");
        var prepared = new FileReference(Path.Combine(pageCache, "prepared.png"));
        var framed = new FileReference(Path.Combine(pageCache, "framed.png"));
        var working = new FileReference(Path.Combine(pageCache, "working-page.png"));
        var finalPage = new FileReference(Path.Combine(processedInteriorDirectory, $"{request.PageId}.png"));
        var cacheStampFile = Path.Combine(processedInteriorDirectory, $"{request.PageId}.input-stamp.json");
        var currentStep = "classification";

        try
        {
            Directory.CreateDirectory(processedInteriorDirectory);
            Directory.CreateDirectory(pageCache);
            var currentStamp = CacheInputStamp.Create(request);
            var previousStamp = await TryReadCacheStampAsync(cacheStampFile, cancellationToken);
            var invalidation = previousStamp is null
                ? CacheInvalidationStage.Classification
                : DetermineInvalidationStage(previousStamp, currentStamp);
            if (invalidation is not CacheInvalidationStage.None)
            {
                ApplyInvalidation(invalidation, classificationFile, prepared, framed, working, finalPage);
                await File.WriteAllTextAsync(cacheStampFile, JsonSerializer.Serialize(currentStamp), cancellationToken);
            }

            var classification = invalidation is CacheInvalidationStage.Classification
                ? null
                : await TryReadClassificationAsync(classificationFile, cancellationToken);
            if (classification is not null && await IsReadableAsync(finalPage, request.FinalPageSize, cancellationToken))
            {
                return new InteriorPageProcessingResult(request.PageId, request.Source, finalPage);
            }

            if (classification is null)
            {
                DeleteDownstream(prepared, framed, working, finalPage);
                currentStep = "classification";
                classification = await artworkClassifier.ClassifyAsync(
                    new ArtworkClassificationRequest(request.Source, request.ArtworkDetectionThreshold), cancellationToken);
                await WriteClassificationAsync(classificationFile, classification, cancellationToken);
            }

            PreparedArtwork preparedArtwork;
            if (!await IsReadableAsync(prepared, request.PreparedArtworkSize, cancellationToken))
            {
                DeleteDownstream(framed, working, finalPage);
                currentStep = "preparation";
                preparedArtwork = await artworkPreparationService.PrepareAsync(new ArtworkPreparationRequest(
                    request.Source,
                    prepared,
                    classification,
                    request.ArtworkDetectionThreshold,
                    request.PreparedArtworkSize,
                    request.TargetDensity), cancellationToken);
                await EnsureSizeAsync(prepared, request.PreparedArtworkSize, "Prepared artwork", cancellationToken);
            }
            else
            {
                preparedArtwork = PreparedArtwork.FromCached(prepared, classification.Type);
            }

            var shouldApplyFrame = ShouldApplyFrame(
                request.Frame is not null && File.Exists(request.Frame.Value),
                request.FrameMode,
                preparedArtwork.AutoFrameRecommended);
            if (!await IsReadableAsync(framed, request.PreparedArtworkSize, cancellationToken) ||
                (!shouldApplyFrame && !HashesMatch(prepared, framed)))
            {
                DeleteDownstream(working, finalPage);
                currentStep = "frame";
                await frameProcessor.ApplyAsync(new FrameOverlayRequest(prepared, framed, request.Frame, shouldApplyFrame), cancellationToken);
                await EnsureSizeAsync(framed, request.PreparedArtworkSize, "Framed artwork", cancellationToken);
            }

            if (!await IsReadableAsync(working, request.WorkingPageSize, cancellationToken))
            {
                DeleteDownstream(finalPage);
                currentStep = "working-page";
                await workingPageProcessor.CenterAsync(
                    new WorkingPageRequest(framed, working, request.WorkingPageSize), cancellationToken);
                await EnsureSizeAsync(working, request.WorkingPageSize, "Working page", cancellationToken);
            }

            if (!await IsReadableAsync(finalPage, request.FinalPageSize, cancellationToken))
            {
                currentStep = "final-page";
                await finalPageProcessor.ProduceAsync(
                    new FinalInteriorPageRequest(working, finalPage, request.FinalPageSize, request.TargetDensity), cancellationToken);
                await EnsureSizeAsync(finalPage, request.FinalPageSize, "Final page", cancellationToken);
            }

            return new InteriorPageProcessingResult(request.PageId, request.Source, finalPage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InteriorPageProcessingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InteriorPageProcessingException(request.PageId, currentStep, exception);
        }
    }

    private async ValueTask EnsureSizeAsync(FileReference image, ImageSize expectedSize, string stage, CancellationToken cancellationToken)
    {
        if (!await IsReadableAsync(image, expectedSize, cancellationToken))
        {
            throw new InvalidDataException($"{stage} must be a readable {expectedSize.Width}x{expectedSize.Height} raster.");
        }
    }

    private async ValueTask<bool> IsReadableAsync(FileReference image, ImageSize expectedSize, CancellationToken cancellationToken)
    {
        if (!File.Exists(image.Value))
        {
            return false;
        }

        try
        {
            return (await imageInspector.GetInfoAsync(image, cancellationToken)).Size == expectedSize;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async ValueTask<CacheInputStamp?> TryReadCacheStampAsync(string cacheStampFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(cacheStampFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cacheStampFile, cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object ||
                !CacheInputStamp.HasRequiredProperties(document.RootElement))
            {
                return null;
            }

            var stamp = JsonSerializer.Deserialize<CacheInputStamp>(json);
            return stamp is { SchemaVersion: CacheStampSchemaVersion } ? stamp : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static CacheInvalidationStage DetermineInvalidationStage(CacheInputStamp previous, CacheInputStamp current)
    {
        if (!ClassificationCompatible(previous, current)) return CacheInvalidationStage.Classification;
        if (!PreparationCompatible(previous, current)) return CacheInvalidationStage.Preparation;
        if (!FrameCompatible(previous, current)) return CacheInvalidationStage.Frame;
        if (!WorkingCompatible(previous, current)) return CacheInvalidationStage.Working;
        if (!FinalCompatible(previous, current)) return CacheInvalidationStage.Final;
        return CacheInvalidationStage.None;
    }

    private static bool ClassificationCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        string.Equals(previous.SourcePath, current.SourcePath, StringComparison.OrdinalIgnoreCase) &&
        previous.SourceLength == current.SourceLength &&
        previous.SourceLastWriteUtcTicks == current.SourceLastWriteUtcTicks &&
        previous.ArtworkDetectionThreshold == current.ArtworkDetectionThreshold &&
        string.Equals(previous.ClassificationAlgorithmVersion, current.ClassificationAlgorithmVersion, StringComparison.Ordinal);

    private static bool PreparationCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        string.Equals(previous.ArtworkPreparationAlgorithmVersion, current.ArtworkPreparationAlgorithmVersion, StringComparison.Ordinal) &&
        previous.PreparedArtworkWidth == current.PreparedArtworkWidth &&
        previous.PreparedArtworkHeight == current.PreparedArtworkHeight &&
        previous.TargetDensityHorizontal == current.TargetDensityHorizontal &&
        previous.TargetDensityVertical == current.TargetDensityVertical;

    private static bool FrameCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        string.Equals(previous.FramePath, current.FramePath, StringComparison.OrdinalIgnoreCase) &&
        previous.FrameLength == current.FrameLength &&
        previous.FrameLastWriteUtcTicks == current.FrameLastWriteUtcTicks &&
        previous.FrameMode == current.FrameMode;

    private static bool WorkingCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        previous.WorkingPageWidth == current.WorkingPageWidth &&
        previous.WorkingPageHeight == current.WorkingPageHeight;

    private static bool FinalCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        previous.FinalPageWidth == current.FinalPageWidth &&
        previous.FinalPageHeight == current.FinalPageHeight;

    private static void ApplyInvalidation(
        CacheInvalidationStage stage,
        string classificationFile,
        FileReference prepared,
        FileReference framed,
        FileReference working,
        FileReference finalPage)
    {
        switch (stage)
        {
            case CacheInvalidationStage.None:
                break;
            case CacheInvalidationStage.Final:
                DeleteIfPresent(finalPage);
                break;
            case CacheInvalidationStage.Working:
                DeleteDownstream(working, finalPage);
                break;
            case CacheInvalidationStage.Frame:
                DeleteDownstream(framed, working, finalPage);
                break;
            case CacheInvalidationStage.Preparation:
                DeleteDownstream(prepared, framed, working, finalPage);
                break;
            case CacheInvalidationStage.Classification:
                DeleteIfPresent(classificationFile);
                DeleteDownstream(prepared, framed, working, finalPage);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
        }
    }

    private static async ValueTask WriteClassificationAsync(string file, ArtworkClassificationResult result, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(ClassificationCacheEntry.From(result)), cancellationToken);

    private static async ValueTask<ArtworkClassificationResult?> TryReadClassificationAsync(string file, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var entry = JsonSerializer.Deserialize<ClassificationCacheEntry>(await File.ReadAllTextAsync(file, cancellationToken));
            return entry?.ToResult();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void DeleteDownstream(params FileReference[] files)
    {
        foreach (var file in files)
        {
            DeleteIfPresent(file);
        }
    }

    private static void DeleteIfPresent(FileReference file)
    {
        if (File.Exists(file.Value))
        {
            File.Delete(file.Value);
        }
    }

    private static void DeleteIfPresent(string file)
    {
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    private static bool ShouldApplyFrame(bool frameAvailable, FrameMode mode, bool autoFrameRecommended) =>
        frameAvailable && (mode switch
        {
            FrameMode.Auto => autoFrameRecommended,
            FrameMode.Enabled => true,
            FrameMode.Disabled => false,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported frame mode.")
        });

    private static bool HashesMatch(FileReference first, FileReference second) =>
        File.ReadAllBytes(first.Value).AsSpan().SequenceEqual(File.ReadAllBytes(second.Value));

    private enum CacheInvalidationStage
    {
        None,
        Final,
        Working,
        Frame,
        Preparation,
        Classification
    }

    private sealed record CacheInputStamp(
        string SourcePath,
        long SourceLength,
        long SourceLastWriteUtcTicks,
        byte ArtworkDetectionThreshold,
        string ClassificationAlgorithmVersion,
        string ArtworkPreparationAlgorithmVersion,
        int PreparedArtworkWidth,
        int PreparedArtworkHeight,
        int WorkingPageWidth,
        int WorkingPageHeight,
        int FinalPageWidth,
        int FinalPageHeight,
        double TargetDensityHorizontal,
        double TargetDensityVertical,
        string? FramePath,
        long FrameLength,
        long FrameLastWriteUtcTicks,
        FrameMode FrameMode,
        string SchemaVersion)
    {
        private static readonly string[] requiredProperties =
        [
            nameof(SourcePath),
            nameof(SourceLength),
            nameof(SourceLastWriteUtcTicks),
            nameof(ArtworkDetectionThreshold),
            nameof(ClassificationAlgorithmVersion),
            nameof(ArtworkPreparationAlgorithmVersion),
            nameof(PreparedArtworkWidth),
            nameof(PreparedArtworkHeight),
            nameof(WorkingPageWidth),
            nameof(WorkingPageHeight),
            nameof(FinalPageWidth),
            nameof(FinalPageHeight),
            nameof(TargetDensityHorizontal),
            nameof(TargetDensityVertical),
            nameof(FramePath),
            nameof(FrameLength),
            nameof(FrameLastWriteUtcTicks),
            nameof(FrameMode),
            nameof(SchemaVersion)
        ];

        public static bool HasRequiredProperties(JsonElement stamp) =>
            requiredProperties.All(property => stamp.TryGetProperty(property, out _));

        public static CacheInputStamp Create(InteriorPagePipelineRequest request)
        {
            var source = new FileInfo(request.Source.Value);
            if (!source.Exists)
            {
                throw new FileNotFoundException("The interior source image does not exist.", request.Source.Value);
            }

            var frame = request.Frame is null ? null : new FileInfo(request.Frame.Value);
            return new CacheInputStamp(
                source.FullName,
                source.Length,
                source.LastWriteTimeUtc.Ticks,
                request.ArtworkDetectionThreshold.Value,
                global::PrintableBook.Core.Application.Processing.ClassificationAlgorithmVersion.Current,
                global::PrintableBook.Core.Application.Processing.ArtworkPreparationAlgorithmVersion.Current,
                request.PreparedArtworkSize.Width,
                request.PreparedArtworkSize.Height,
                request.WorkingPageSize.Width,
                request.WorkingPageSize.Height,
                request.FinalPageSize.Width,
                request.FinalPageSize.Height,
                request.TargetDensity.Horizontal,
                request.TargetDensity.Vertical,
                request.Frame?.Value,
                frame?.Exists == true ? frame.Length : 0,
                frame?.Exists == true ? frame.LastWriteTimeUtc.Ticks : 0,
                request.FrameMode,
                CacheStampSchemaVersion);
        }
    }

    private sealed record ClassificationCacheEntry(
        string Version,
        string Type,
        BorderLineCacheEntry BorderLine,
        BorderPixelCacheEntry? BorderPixel)
    {
        public static ClassificationCacheEntry From(ArtworkClassificationResult result) => new(
            ClassificationAlgorithmVersion.Current,
            ToCanonicalType(result.Type),
            BorderLineCacheEntry.From(result.BorderLine),
            result.BorderPixel is null ? null : BorderPixelCacheEntry.From(result.BorderPixel));

        public ArtworkClassificationResult ToResult()
        {
            if (!string.Equals(Version, ClassificationAlgorithmVersion.Current, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cached classification uses an incompatible algorithm version.");
            }

            return new ArtworkClassificationResult(FromCanonicalType(Type), BorderLine.ToResult(), BorderPixel?.ToResult());
        }

        private static string ToCanonicalType(ArtworkType type) => type switch
        {
            ArtworkType.BorderArt => "borderart",
            ArtworkType.FullArt => "fullart",
            ArtworkType.CropArt => "cropart",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported artwork type.")
        };

        private static ArtworkType FromCanonicalType(string type) => type switch
        {
            "borderart" => ArtworkType.BorderArt,
            "fullart" => ArtworkType.FullArt,
            "cropart" => ArtworkType.CropArt,
            _ => throw new InvalidOperationException("Cached classification has an unknown artwork type.")
        };
    }

    private sealed record BorderLineCacheEntry(bool HasBorder, int? Left, int? Right, int? Top, int? Bottom)
    {
        public static BorderLineCacheEntry From(BorderLineDetectionResult result) => new(
            result.HasBorder,
            result.Left.Position,
            result.Right.Position,
            result.Top.Position,
            result.Bottom.Position);

        public BorderLineDetectionResult ToResult()
        {
            if (!HasBorder)
            {
                return BorderLineDetectionResult.NoBorder();
            }

            if (Left is null || Right is null || Top is null || Bottom is null || Right < Left || Bottom < Top)
            {
                throw new InvalidOperationException("Cached BorderArt evidence has invalid bounds.");
            }

            return BorderLineDetectionResult.Detected(
                BorderLineSideResult.Detected(Left.Value),
                BorderLineSideResult.Detected(Right.Value),
                BorderLineSideResult.Detected(Top.Value),
                BorderLineSideResult.Detected(Bottom.Value),
                new ImageRectangle(new ImagePoint(Left.Value, Top.Value), new ImageSize(Right.Value - Left.Value + 1, Bottom.Value - Top.Value + 1)));
        }
    }

    private sealed record BorderPixelCacheEntry(bool HasBorderPixel, bool LeftHit, bool RightHit, bool TopHit, bool BottomHit)
    {
        public static BorderPixelCacheEntry From(BorderPixelDetectionResult result) => new(
            result.HasBorderPixel,
            result.LeftHit,
            result.RightHit,
            result.TopHit,
            result.BottomHit);

        public BorderPixelDetectionResult ToResult()
        {
            var result = BorderPixelDetectionResult.Detected(LeftHit, RightHit, TopHit, BottomHit);
            if (result.HasBorderPixel != HasBorderPixel)
            {
                throw new InvalidOperationException("Cached BorderPixel evidence is inconsistent.");
            }

            return result;
        }
    }
}
