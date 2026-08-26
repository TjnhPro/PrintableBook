namespace PrintableBook.Core.Application.Processing;

/// <summary>Controls the deterministic, square raster shared by every interior processing stage.</summary>
public sealed record ArtworkSourceNormalizationSettings(int NormalizedSourceSize)
{
    public static ArtworkSourceNormalizationSettings Default { get; } = new(2048);
}
