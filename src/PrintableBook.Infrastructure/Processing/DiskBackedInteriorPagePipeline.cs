using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Processing;

/// <summary>
/// Persists every sequential image stage so large image buffers can be released before the next stage opens its input.
/// </summary>
public sealed class DiskBackedInteriorPagePipeline(
    IArtworkTrimProcessor trimProcessor,
    ISquareCanvasProcessor squareCanvasProcessor,
    IArtworkResizeProcessor resizeProcessor,
    IFrameProcessor frameProcessor,
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

        var pageCache = Path.Combine(request.Workspace.WorkingDirectory.Value, "cache", request.PageId);
        var processedInteriorDirectory = Path.Combine(request.Workspace.ProcessedDirectory.Value, "interior");
        Directory.CreateDirectory(processedInteriorDirectory);
        Directory.CreateDirectory(pageCache);
        var trimmed = new FileReference(Path.Combine(pageCache, "trim.png"));
        var canvas = new FileReference(Path.Combine(pageCache, "canvas.png"));
        var resized = new FileReference(Path.Combine(pageCache, "resize.png"));
        var framed = new FileReference(Path.Combine(pageCache, "frame.png"));
        var finalPage = new FileReference(Path.Combine(processedInteriorDirectory, $"{request.PageId}.png"));
        var cacheStampFile = Path.Combine(processedInteriorDirectory, $"{request.PageId}.input-stamp.json");
        var cacheStamp = CacheInputStamp.Create(request);

        try
        {
            var hasMatchingStamp = await HasMatchingStampAsync(cacheStampFile, cacheStamp, cancellationToken);
            if (hasMatchingStamp && await IsReadableAsync(finalPage, request.TargetSize, cancellationToken))
            {
                return new InteriorPageProcessingResult(request.PageId, request.Source, finalPage);
            }

            if (!hasMatchingStamp)
            {
                Directory.Delete(pageCache, recursive: true);
                Directory.CreateDirectory(pageCache);
                DeleteDownstream(finalPage);
                await File.WriteAllTextAsync(cacheStampFile, JsonSerializer.Serialize(cacheStamp), cancellationToken);
            }

            if (!await IsReadableAsync(trimmed, null, cancellationToken))
            {
                DeleteDownstream(canvas, resized, framed, finalPage);
                var trimResult = await trimProcessor.TrimAsync(
                    new ArtworkTrimRequest(request.Source, trimmed, request.ArtworkDetectionThreshold), cancellationToken);
                if (!trimResult.HasArtwork)
                {
                    throw new InvalidDataException("No black or near-black artwork was detected.");
                }
            }

            if (!await IsReadableAsync(canvas, null, cancellationToken))
            {
                DeleteDownstream(resized, framed, finalPage);
                await squareCanvasProcessor.NormalizeAsync(new SquareCanvasRequest(trimmed, canvas), cancellationToken);
            }

            if (!await IsReadableAsync(resized, request.TargetSize, cancellationToken))
            {
                DeleteDownstream(framed, finalPage);
                await resizeProcessor.ResizeAsync(
                    new ArtworkResizeRequest(canvas, resized, request.TargetSize.Width, request.TargetDensity), cancellationToken);
            }

            if (!await IsReadableAsync(framed, request.TargetSize, cancellationToken))
            {
                DeleteDownstream(finalPage);
                await frameProcessor.ApplyAsync(
                    new FrameOverlayRequest(resized, framed, request.Frame, request.IsFrameEnabled), cancellationToken);
            }

            if (!await IsReadableAsync(finalPage, request.TargetSize, cancellationToken))
            {
                await finalPageProcessor.ProduceAsync(
                    new FinalInteriorPageRequest(framed, finalPage, request.TargetSize, request.TargetDensity), cancellationToken);
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
            throw new InteriorPageProcessingException(request.PageId, FindCurrentStep(trimmed, canvas, resized, framed), exception);
        }
    }

    private static string FindCurrentStep(FileReference trimmed, FileReference canvas, FileReference resized, FileReference framed) =>
        !File.Exists(trimmed.Value) ? "trim" :
        !File.Exists(canvas.Value) ? "canvas" :
        !File.Exists(resized.Value) ? "resize" :
        !File.Exists(framed.Value) ? "frame" :
        "final-page";

    private async ValueTask<bool> IsReadableAsync(FileReference image, ImageSize? expectedSize, CancellationToken cancellationToken)
    {
        if (!File.Exists(image.Value))
        {
            return false;
        }

        try
        {
            var info = await imageInspector.GetInfoAsync(image, cancellationToken);
            return expectedSize is null || info.Size == expectedSize.Value;
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

    private static void DeleteDownstream(params FileReference[] files)
    {
        foreach (var file in files)
        {
            if (File.Exists(file.Value))
            {
                File.Delete(file.Value);
            }
        }
    }

    private sealed record CacheInputStamp(
        string SourcePath,
        long SourceLength,
        long SourceLastWriteUtcTicks,
        byte Threshold,
        ImageSize TargetSize,
        ImageDensity TargetDensity,
        string? FramePath,
        long FrameLength,
        long FrameLastWriteUtcTicks,
        bool IsFrameEnabled)
    {
        public static CacheInputStamp Create(InteriorPagePipelineRequest request)
        {
            var source = new FileInfo(request.Source.Value);
            var frame = request.Frame is null ? null : new FileInfo(request.Frame.Value);
            return new CacheInputStamp(
                source.FullName,
                source.Length,
                source.LastWriteTimeUtc.Ticks,
                request.ArtworkDetectionThreshold.Value,
                request.TargetSize,
                request.TargetDensity,
                request.Frame?.Value,
                frame?.Exists == true ? frame.Length : 0,
                frame?.Exists == true ? frame.LastWriteTimeUtc.Ticks : 0,
                request.IsFrameEnabled);
        }
    }
}
