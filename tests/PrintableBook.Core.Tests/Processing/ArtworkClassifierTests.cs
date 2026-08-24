using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class ArtworkClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_returns_border_art_and_does_not_invoke_border_pixel_when_border_line_is_detected()
    {
        var borderLine = CreateBorderLine();
        var borderLineDetector = new RecordingBorderLineDetector((_, _) => ValueTask.FromResult(borderLine));
        var borderPixelDetector = new RecordingBorderPixelDetector((_, _) =>
            throw new Xunit.Sdk.XunitException("BorderPixel must not run after a positive BorderLine result."));
        var classifier = new ArtworkClassifier(borderLineDetector, borderPixelDetector);

        var result = await classifier.ClassifyAsync(CreateRequest());

        Assert.Equal(ArtworkType.BorderArt, result.Type);
        Assert.Same(borderLine, result.BorderLine);
        Assert.Null(result.BorderPixel);
        Assert.Equal(1, borderLineDetector.CallCount);
        Assert.Equal(0, borderPixelDetector.CallCount);
    }

    [Fact]
    public async Task ClassifyAsync_returns_full_art_and_preserves_pixel_evidence_when_only_border_pixel_is_detected()
    {
        var borderLine = BorderLineDetectionResult.NoBorder();
        var borderPixel = BorderPixelDetectionResult.Detected(false, true, true, false);
        var classifier = new ArtworkClassifier(
            new RecordingBorderLineDetector((_, _) => ValueTask.FromResult(borderLine)),
            new RecordingBorderPixelDetector((_, _) => ValueTask.FromResult(borderPixel)));

        var result = await classifier.ClassifyAsync(CreateRequest());

        Assert.Equal(ArtworkType.FullArt, result.Type);
        Assert.Same(borderLine, result.BorderLine);
        Assert.Same(borderPixel, result.BorderPixel);
        Assert.True(result.BorderPixel!.RightHit);
        Assert.True(result.BorderPixel.TopHit);
    }

    [Fact]
    public async Task ClassifyAsync_returns_crop_art_and_preserves_negative_pixel_evidence()
    {
        var borderLine = BorderLineDetectionResult.NoBorder();
        var borderPixel = BorderPixelDetectionResult.None();
        var classifier = new ArtworkClassifier(
            new RecordingBorderLineDetector((_, _) => ValueTask.FromResult(borderLine)),
            new RecordingBorderPixelDetector((_, _) => ValueTask.FromResult(borderPixel)));

        var result = await classifier.ClassifyAsync(CreateRequest());

        Assert.Equal(ArtworkType.CropArt, result.Type);
        Assert.Same(borderLine, result.BorderLine);
        Assert.Same(borderPixel, result.BorderPixel);
        Assert.False(result.BorderPixel!.HasBorderPixel);
    }

    [Fact]
    public async Task ClassifyAsync_propagates_border_line_failure_without_running_border_pixel()
    {
        var failure = new InvalidOperationException("border-line failed");
        var borderLineDetector = new RecordingBorderLineDetector((_, _) => ValueTask.FromException<BorderLineDetectionResult>(failure));
        var borderPixelDetector = new RecordingBorderPixelDetector((_, _) => ValueTask.FromResult(BorderPixelDetectionResult.None()));
        var classifier = new ArtworkClassifier(borderLineDetector, borderPixelDetector);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => classifier.ClassifyAsync(CreateRequest()).AsTask());

        Assert.Same(failure, actual);
        Assert.Equal(0, borderPixelDetector.CallCount);
    }

    [Fact]
    public async Task ClassifyAsync_propagates_border_pixel_failure_after_a_negative_border_line()
    {
        var failure = new InvalidOperationException("border-pixel failed");
        var borderLineDetector = new RecordingBorderLineDetector((_, _) => ValueTask.FromResult(BorderLineDetectionResult.NoBorder()));
        var borderPixelDetector = new RecordingBorderPixelDetector((_, _) => ValueTask.FromException<BorderPixelDetectionResult>(failure));
        var classifier = new ArtworkClassifier(borderLineDetector, borderPixelDetector);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => classifier.ClassifyAsync(CreateRequest()).AsTask());

        Assert.Same(failure, actual);
        Assert.Equal(1, borderLineDetector.CallCount);
        Assert.Equal(1, borderPixelDetector.CallCount);
    }

    [Fact]
    public async Task ClassifyAsync_propagates_the_cancellation_token_to_the_first_detector()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var borderLineDetector = new RecordingBorderLineDetector((_, token) =>
            ValueTask.FromCanceled<BorderLineDetectionResult>(token));
        var borderPixelDetector = new RecordingBorderPixelDetector((_, _) => ValueTask.FromResult(BorderPixelDetectionResult.None()));
        var classifier = new ArtworkClassifier(borderLineDetector, borderPixelDetector);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            classifier.ClassifyAsync(CreateRequest(), cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, borderLineDetector.LastCancellationToken);
        Assert.Equal(0, borderPixelDetector.CallCount);
    }

    private static ArtworkClassificationRequest CreateRequest() =>
        new(new FileReference("source.png"), new ArtworkDetectionThreshold(20));

    private static BorderLineDetectionResult CreateBorderLine() =>
        BorderLineDetectionResult.Detected(
            BorderLineSideResult.Detected(10),
            BorderLineSideResult.Detected(90),
            BorderLineSideResult.Detected(10),
            BorderLineSideResult.Detected(90),
            new ImageRectangle(new ImagePoint(10, 10), new ImageSize(81, 81)));

    private sealed class RecordingBorderLineDetector(
        Func<BorderLineDetectionRequest, CancellationToken, ValueTask<BorderLineDetectionResult>> handler) : IBorderLineDetector
    {
        public int CallCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask<BorderLineDetectionResult> DetectAsync(
            BorderLineDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCancellationToken = cancellationToken;
            return handler(request, cancellationToken);
        }
    }

    private sealed class RecordingBorderPixelDetector(
        Func<BorderPixelDetectionRequest, CancellationToken, ValueTask<BorderPixelDetectionResult>> handler) : IBorderPixelDetector
    {
        public int CallCount { get; private set; }

        public ValueTask<BorderPixelDetectionResult> DetectAsync(
            BorderPixelDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return handler(request, cancellationToken);
        }
    }
}
