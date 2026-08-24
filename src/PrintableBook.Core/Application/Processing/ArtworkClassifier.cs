namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Applies the locked classification order to detector evidence without performing raster work.
/// </summary>
public sealed class ArtworkClassifier : IArtworkClassifier
{
    private readonly IBorderLineDetector borderLineDetector;
    private readonly IBorderPixelDetector borderPixelDetector;

    public ArtworkClassifier(
        IBorderLineDetector borderLineDetector,
        IBorderPixelDetector borderPixelDetector)
    {
        this.borderLineDetector = borderLineDetector ?? throw new ArgumentNullException(nameof(borderLineDetector));
        this.borderPixelDetector = borderPixelDetector ?? throw new ArgumentNullException(nameof(borderPixelDetector));
    }

    public async ValueTask<ArtworkClassificationResult> ClassifyAsync(
        ArtworkClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var borderLine = await borderLineDetector.DetectAsync(
            new BorderLineDetectionRequest(request.Source, request.Threshold),
            cancellationToken);

        if (borderLine.HasBorder)
        {
            return new ArtworkClassificationResult(ArtworkType.BorderArt, borderLine, null);
        }

        var borderPixel = await borderPixelDetector.DetectAsync(
            new BorderPixelDetectionRequest(request.Source, request.Threshold),
            cancellationToken);

        var type = borderPixel.HasBorderPixel
            ? ArtworkType.FullArt
            : ArtworkType.CropArt;

        return new ArtworkClassificationResult(type, borderLine, borderPixel);
    }
}
