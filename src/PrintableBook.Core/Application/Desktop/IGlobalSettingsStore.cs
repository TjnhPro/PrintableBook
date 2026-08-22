namespace PrintableBook.Core.Application.Desktop;

public sealed record GlobalSettings(int MaximumPageConcurrency, byte ArtworkDetectionThreshold, int ArtworkMaximumSide, int WorkingPageWidth, int WorkingPageHeight, int FinalPageWidth, int FinalPageHeight, int Dpi, double InteriorPdfWidthInches, double InteriorPdfHeightInches)
{
    public static GlobalSettings Default { get; } = new(4, 20, 2270, 2550, 2550, 2588, 2625, 300, 8.5, 8.5);
}

public interface IGlobalSettingsStore
{
    ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default);
}
