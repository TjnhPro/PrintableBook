using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Physical PDF page dimensions, expressed in inches and independent of raster pixels.
/// </summary>
public readonly record struct PhysicalPageSize(double WidthInches, double HeightInches)
{
    public double WidthInPoints => WidthInches * 72d;

    public double HeightInPoints => HeightInches * 72d;
}

public static class PreviewPdfRasterSizes
{
    public static ImageSize Cover { get; } = new(2726, 1313);

    public static ImageSize Interior { get; } = new(600, 609);
}

public static class CoverPanelPreviewContract
{
    public const int SplitX = 2621;

    public const int SourceWidth = 5242;

    public const int SourceHeight = 2626;

    public const int JpegQuality = 90;

    public const string BackFileName = "back_cover.jpg";

    public const string FrontFileName = "front_cover.jpg";

    public const string UnavailableWarningCode = "cover_panel_previews_unavailable";

    public static ImageSize PreviewSize { get; } = new(1198, 1200);
}

public enum CoverPanelPreviewStatus
{
    Ready = 0,
    Unavailable = 1
}

public sealed record CoverPanelPreviewPair(
    FileReference BackCover,
    FileReference FrontCover);

public sealed record CoverPanelPreviewOutcome(
    CoverPanelPreviewStatus Status,
    CoverPanelPreviewPair? Pair = null,
    string? WarningCode = null)
{
    public static CoverPanelPreviewOutcome Ready(CoverPanelPreviewPair pair) =>
        new(CoverPanelPreviewStatus.Ready, pair);

    public static CoverPanelPreviewOutcome Unavailable(string warningCode = CoverPanelPreviewContract.UnavailableWarningCode) =>
        new(CoverPanelPreviewStatus.Unavailable, WarningCode: warningCode);
}

public sealed record PrintableBookPdfExportRequest(
    FileReference Cover,
    IReadOnlyList<FileReference> IntroPages,
    IReadOnlyList<FileReference> OrderedInteriorPages,
    FileReference? BackgroundPage,
    DirectoryReference TemporaryOutputDirectory,
    PhysicalPageSize CoverPageSize,
    PhysicalPageSize InteriorPageSize,
    int MaximumPageConcurrency,
    IReadOnlyList<FileReference>? ProductionPrefixPages = null)
{
    public IReadOnlyList<FileReference> EffectiveProductionPrefixPages => ProductionPrefixPages ?? [];
}

public sealed record PrintableBookPdfExportResult(
    FileReference CoverPdf,
    FileReference InteriorPdf,
    FileReference? CoverPreviewPdf = null,
    FileReference? InteriorPreviewPdf = null);

public sealed record CoverPdfExportRequest(
    FileReference Cover,
    DirectoryReference TemporaryOutputDirectory,
    PhysicalPageSize CoverPageSize);

public sealed record CoverPdfExportResult(
    FileReference CoverPdf,
    FileReference? PreviewPdf = null,
    CoverPanelPreviewOutcome? PanelPreviews = null);

public sealed record InteriorPdfExportRequest(
    IReadOnlyList<FileReference> IntroPages,
    IReadOnlyList<FileReference> OrderedInteriorPages,
    FileReference? BackgroundPage,
    DirectoryReference TemporaryOutputDirectory,
    PhysicalPageSize InteriorPageSize,
    int MaximumPageConcurrency,
    IReadOnlyList<FileReference>? ProductionPrefixPages = null)
{
    public IReadOnlyList<FileReference> EffectiveProductionPrefixPages => ProductionPrefixPages ?? [];
}

public sealed record InteriorPdfExportResult(FileReference InteriorPdf, FileReference? PreviewPdf = null);

public interface IPrintableBookPdfExporter
{
    ValueTask<PrintableBookPdfExportResult> ExportAsync(
        PrintableBookPdfExportRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InteriorPdfExportResult> ExportInteriorAsync(
        InteriorPdfExportRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<CoverPdfExportResult> ExportCoverAsync(
        CoverPdfExportRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Cover-only PDF export is not supported by this adapter.");
}
