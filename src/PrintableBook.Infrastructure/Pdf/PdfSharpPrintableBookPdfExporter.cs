using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Pdf;

public sealed class MagickPrintableBookPdfExporter : IPrintableBookPdfExporter
{
    public ValueTask<PrintableBookPdfExportResult> ExportAsync(
        PrintableBookPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.OrderedInteriorPages.Count == 0)
        {
            throw new ArgumentException("At least one interior page is required for PDF export.", nameof(request));
        }

        if (request.CoverPageSize.WidthInches <= 0 || request.CoverPageSize.HeightInches <= 0 ||
            request.InteriorPageSize.WidthInches <= 0 || request.InteriorPageSize.HeightInches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "PDF page dimensions must be positive.");
        }

        Directory.CreateDirectory(request.TemporaryOutputDirectory.Value);
        var coverPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "cover.pdf"));
        var interiorPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf"));
        WritePdf(coverPdf, [request.Cover], request.CoverPageSize, cancellationToken);
        WritePdf(interiorPdf, request.OrderedInteriorPages, request.InteriorPageSize, cancellationToken);
        return ValueTask.FromResult(new PrintableBookPdfExportResult(coverPdf, interiorPdf));
    }

    private static void WritePdf(
        FileReference target,
        IReadOnlyList<FileReference> pages,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        using var document = new MagickImageCollection();
        foreach (var source in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = new MagickImage(source.Value);
            image.Density = new Density(
                image.Width / pageSize.WidthInches,
                image.Height / pageSize.HeightInches,
                DensityUnit.PixelsPerInch);
            document.Add(image);
        }

        document.Write(target.Value, MagickFormat.Pdf);
    }
}
