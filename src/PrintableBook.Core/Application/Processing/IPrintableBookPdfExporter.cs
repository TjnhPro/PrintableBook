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
    int MaximumPageConcurrency);

public sealed record PrintableBookPdfExportResult(FileReference CoverPdf, FileReference InteriorPdf);

public sealed record InteriorPdfExportRequest(
    IReadOnlyList<FileReference> IntroPages,
    IReadOnlyList<FileReference> OrderedInteriorPages,
    FileReference? BackgroundPage,
    DirectoryReference TemporaryOutputDirectory,
    PhysicalPageSize InteriorPageSize,
    int MaximumPageConcurrency);

public sealed record InteriorPdfExportResult(FileReference InteriorPdf);

public interface IPrintableBookPdfExporter
{
    ValueTask<PrintableBookPdfExportResult> ExportAsync(
        PrintableBookPdfExportRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InteriorPdfExportResult> ExportInteriorAsync(
        InteriorPdfExportRequest request,
        CancellationToken cancellationToken = default);
}
