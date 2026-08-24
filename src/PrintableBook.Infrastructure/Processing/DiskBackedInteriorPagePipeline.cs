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
            var cacheStamp = CacheInputStamp.Create(request);
            var hasMatchingStamp = await HasMatchingStampAsync(cacheStampFile, cacheStamp, cancellationToken);
            if (!hasMatchingStamp)
            {
                Directory.Delete(pageCache, recursive: true);
                Directory.CreateDirectory(pageCache);
                DeleteIfPresent(finalPage);
                await File.WriteAllTextAsync(cacheStampFile, JsonSerializer.Serialize(cacheStamp), cancellationToken);
            }

            var classification = hasMatchingStamp
                ? await TryReadClassificationAsync(classificationFile, cancellationToken)
                : null;
            if (hasMatchingStamp && classification is not null && await IsReadableAsync(finalPage, request.FinalPageSize, cancellationToken))
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

            var shouldApplyFrame = request.Frame is not null &&
                File.Exists(request.Frame.Value) &&
                request.IsFrameEnabled &&
                preparedArtwork.AutoFrameRecommended;
            if (!await IsReadableAsync(framed, request.PreparedArtworkSize, cancellationToken) || !preparedArtwork.AutoFrameRecommended)
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

    private static async ValueTask<bool> HasMatchingStampAsync(string cacheStampFile, CacheInputStamp expected, CancellationToken cancellationToken)
    {
        if (!File.Exists(cacheStampFile))
        {
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cacheStampFile, cancellationToken);
            return string.Equals(json, JsonSerializer.Serialize(expected), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
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

    private sealed record CacheInputStamp(
        string SourcePath,
        long SourceLength,
        long SourceLastWriteUtcTicks,
        byte ArtworkDetectionThreshold,
        string ClassificationAlgorithmVersion,
        string ArtworkPreparationAlgorithmVersion,
        ImageSize PreparedArtworkSize,
        ImageSize WorkingPageSize,
        ImageSize FinalPageSize,
        ImageDensity TargetDensity,
        string? FramePath,
        long FrameLength,
        long FrameLastWriteUtcTicks,
        bool FrameEnabled)
    {
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
                request.PreparedArtworkSize,
                request.WorkingPageSize,
                request.FinalPageSize,
                request.TargetDensity,
                request.Frame?.Value,
                frame?.Exists == true ? frame.Length : 0,
                frame?.Exists == true ? frame.LastWriteTimeUtc.Ticks : 0,
                request.IsFrameEnabled);
        }
    }

    private sealed record ClassificationCacheEntry(
        string Version,
        ArtworkType Type,
        BorderLineCacheEntry BorderLine,
        BorderPixelCacheEntry? BorderPixel)
    {
        public static ClassificationCacheEntry From(ArtworkClassificationResult result) => new(
            ClassificationAlgorithmVersion.Current,
            result.Type,
            BorderLineCacheEntry.From(result.BorderLine),
            result.BorderPixel is null ? null : BorderPixelCacheEntry.From(result.BorderPixel));

        public ArtworkClassificationResult ToResult()
        {
            if (!string.Equals(Version, ClassificationAlgorithmVersion.Current, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cached classification uses an incompatible algorithm version.");
            }

            return new ArtworkClassificationResult(Type, BorderLine.ToResult(), BorderPixel?.ToResult());
        }
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
