namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Describes the artwork type used by preparation and why that type was selected.
/// Detector evidence is present only when detection actually completed.
/// </summary>
public sealed record EffectiveArtworkClassification
{
    private EffectiveArtworkClassification(
        ArtworkType type,
        ArtworkClassificationOrigin origin,
        ArtworkClassificationResult? detection)
    {
        if (origin == ArtworkClassificationOrigin.Detected)
        {
            ArgumentNullException.ThrowIfNull(detection);
            if (type != detection.Type)
            {
                throw new ArgumentException("The effective artwork type must agree with detector evidence.", nameof(type));
            }
        }
        else
        {
            if (detection is not null)
            {
                throw new ArgumentException("Forced artwork decisions must not contain detector evidence.", nameof(detection));
            }

            if (type != ArtworkType.CropArt)
            {
                throw new ArgumentException("Forced artwork decisions must use CropArt preparation.", nameof(type));
            }
        }

        Type = type;
        Origin = origin;
        Detection = detection;
    }

    public ArtworkType Type { get; }

    public ArtworkClassificationOrigin Origin { get; }

    public ArtworkDetectionStatus DetectionStatus =>
        Origin == ArtworkClassificationOrigin.Detected
            ? ArtworkDetectionStatus.Completed
            : ArtworkDetectionStatus.NotRun;

    public ArtworkClassificationResult? Detection { get; }

    public static EffectiveArtworkClassification FromDetection(ArtworkClassificationResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        return new EffectiveArtworkClassification(detection.Type, ArtworkClassificationOrigin.Detected, detection);
    }

    public static implicit operator EffectiveArtworkClassification(ArtworkClassificationResult detection) =>
        FromDetection(detection);

    public static EffectiveArtworkClassification ForcedNoFrame() =>
        new(ArtworkType.CropArt, ArtworkClassificationOrigin.ForcedNoFrame, null);

    public static EffectiveArtworkClassification ForcedIntro() =>
        new(ArtworkType.CropArt, ArtworkClassificationOrigin.ForcedIntro, null);
}

public enum ArtworkClassificationOrigin
{
    Detected = 0,
    ForcedNoFrame = 1,
    ForcedIntro = 2
}

public enum ArtworkDetectionStatus
{
    Completed = 0,
    NotRun = 1
}
