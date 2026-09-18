using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class EffectiveArtworkClassificationTests
{
    [Fact]
    public void FromDetection_preserves_real_detector_evidence()
    {
        var detected = new ArtworkClassificationResult(
            ArtworkType.CropArt,
            BorderLineDetectionResult.NoBorder(),
            BorderPixelDetectionResult.None());

        var decision = EffectiveArtworkClassification.FromDetection(detected);

        Assert.Equal(ArtworkType.CropArt, decision.Type);
        Assert.Equal(ArtworkClassificationOrigin.Detected, decision.Origin);
        Assert.Equal(ArtworkDetectionStatus.Completed, decision.DetectionStatus);
        Assert.Same(detected, decision.Detection);
    }

    [Fact]
    public void ForcedNoFrame_is_crop_art_without_detector_evidence()
    {
        var decision = EffectiveArtworkClassification.ForcedNoFrame();

        Assert.Equal(ArtworkType.CropArt, decision.Type);
        Assert.Equal(ArtworkClassificationOrigin.ForcedNoFrame, decision.Origin);
        Assert.Equal(ArtworkDetectionStatus.NotRun, decision.DetectionStatus);
        Assert.Null(decision.Detection);
    }

    [Fact]
    public void ForcedIntro_is_crop_art_without_detector_evidence()
    {
        var decision = EffectiveArtworkClassification.ForcedIntro();

        Assert.Equal(ArtworkType.CropArt, decision.Type);
        Assert.Equal(ArtworkClassificationOrigin.ForcedIntro, decision.Origin);
        Assert.Equal(ArtworkDetectionStatus.NotRun, decision.DetectionStatus);
        Assert.Null(decision.Detection);
    }
}
