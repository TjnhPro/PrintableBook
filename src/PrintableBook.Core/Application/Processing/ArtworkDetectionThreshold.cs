namespace PrintableBook.Core.Application.Processing;

public readonly record struct ArtworkDetectionThreshold
{
    public ArtworkDetectionThreshold(byte value)
    {
        Value = value;
    }

    public byte Value { get; }
}
