namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Version of the detector ordering and detected artwork-type decision.
/// Forced preparation policies are versioned independently by the page pipeline.
/// </summary>
public static class ClassificationAlgorithmVersion
{
    public const string Current = "artwork-classification-v2";
}
