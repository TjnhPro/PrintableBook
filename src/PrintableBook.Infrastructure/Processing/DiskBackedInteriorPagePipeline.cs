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
    IFinalInteriorPageProcessor finalPageProcessor) : IInteriorPagePipeline
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
        Directory.CreateDirectory(pageCache);
        var trimmed = new FileReference(Path.Combine(pageCache, "trim.png"));
        var canvas = new FileReference(Path.Combine(pageCache, "canvas.png"));
        var resized = new FileReference(Path.Combine(pageCache, "resize.png"));
        var framed = new FileReference(Path.Combine(pageCache, "frame.png"));
        var finalPage = new FileReference(Path.Combine(request.Workspace.OutputDirectory.Value, "interior", $"{request.PageId}.png"));

        try
        {
            var trimResult = await trimProcessor.TrimAsync(
                new ArtworkTrimRequest(request.Source, trimmed, request.ArtworkDetectionThreshold), cancellationToken);
            if (!trimResult.HasArtwork)
            {
                throw new InvalidDataException("No black or near-black artwork was detected.");
            }

            await squareCanvasProcessor.NormalizeAsync(new SquareCanvasRequest(trimmed, canvas), cancellationToken);
            await resizeProcessor.ResizeAsync(
                new ArtworkResizeRequest(canvas, resized, request.TargetSize, request.TargetDensity), cancellationToken);
            await frameProcessor.ApplyAsync(
                new FrameOverlayRequest(resized, framed, request.Frame, request.IsFrameEnabled), cancellationToken);
            await finalPageProcessor.ProduceAsync(
                new FinalInteriorPageRequest(framed, finalPage, request.TargetSize, request.TargetDensity), cancellationToken);

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
}
