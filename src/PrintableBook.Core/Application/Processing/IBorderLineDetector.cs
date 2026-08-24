namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Detects a continuous near-black line on each outer side of an artwork image.
/// </summary>
public interface IBorderLineDetector
{
    ValueTask<BorderLineDetectionResult> DetectAsync(
        BorderLineDetectionRequest request,
        CancellationToken cancellationToken = default);
}
