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

public sealed record PrintableBookPdfExportResult(FileReference CoverPdf, FileReference InteriorPdf);

public sealed record CoverPdfExportRequest(
    FileReference Cover,
    DirectoryReference TemporaryOutputDirectory,
    PhysicalPageSize CoverPageSize);

public sealed record CoverPdfExportResult(FileReference CoverPdf);

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

public sealed record InteriorPdfExportResult(FileReference InteriorPdf);

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
