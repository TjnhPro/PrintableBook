using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Input for read-only outer border-line detection.
/// </summary>
public sealed record BorderLineDetectionRequest(
    FileReference Source,
    ArtworkDetectionThreshold Threshold,
    BorderLineDetectionSettings? Settings = null)
{
    public BorderLineDetectionSettings EffectiveSettings => Settings ?? BorderLineDetectionSettings.Default;
}
