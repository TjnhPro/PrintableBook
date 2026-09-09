using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Pdf;

public sealed class PdfSharpPrintableBookPdfExporter : IPrintableBookPdfExporter
{
    public ValueTask<PrintableBookPdfExportResult> ExportAsync(
        PrintableBookPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request, cancellationToken);
        Directory.CreateDirectory(request.TemporaryOutputDirectory.Value);

        var coverPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "cover.pdf"));
        var interiorPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf"));
        WriteSingleRasterPdf(coverPdf, request.Cover, request.CoverPageSize, cancellationToken);
        WriteInteriorPdfSequential(
            interiorPdf,
            request.IntroPages,
            request.OrderedInteriorPages,
            request.BackgroundPage,
            request.InteriorPageSize,
            cancellationToken);

        return ValueTask.FromResult(new PrintableBookPdfExportResult(coverPdf, interiorPdf));
    }

    public ValueTask<InteriorPdfExportResult> ExportInteriorAsync(
        InteriorPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request, cancellationToken);
        Directory.CreateDirectory(request.TemporaryOutputDirectory.Value);

        var interiorPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf"));
        WriteInteriorPdfSequential(
            interiorPdf,
            request.IntroPages,
            request.OrderedInteriorPages,
            request.BackgroundPage,
            request.InteriorPageSize,
            cancellationToken);

        return ValueTask.FromResult(new InteriorPdfExportResult(interiorPdf));
    }

    private static void Validate(PrintableBookPdfExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInteriorRequest(
            request.OrderedInteriorPages,
            request.InteriorPageSize,
            request.MaximumPageConcurrency);

        if (request.CoverPageSize.WidthInches <= 0 || request.CoverPageSize.HeightInches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "PDF page dimensions must be positive.");
        }
    }

    private static void Validate(InteriorPdfExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInteriorRequest(
            request.OrderedInteriorPages,
            request.InteriorPageSize,
            request.MaximumPageConcurrency);
    }

    private static void ValidateInteriorRequest(
        IReadOnlyList<FileReference> orderedInteriorPages,
        PhysicalPageSize interiorPageSize,
        int maximumPageConcurrency)
    {
        if (orderedInteriorPages.Count == 0)
        {
            throw new ArgumentException("At least one interior artwork page is required for PDF export.");
        }

        if (interiorPageSize.WidthInches <= 0 || interiorPageSize.HeightInches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interiorPageSize), "PDF page dimensions must be positive.");
        }

        if (maximumPageConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPageConcurrency), "Maximum page concurrency must be positive.");
        }
    }

    private static void WriteSingleRasterPdf(
        FileReference target,
        FileReference source,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        using var document = new PdfDocument();
        AddRasterPage(document, source, pageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        document.Save(target.Value);
    }

    private static void WriteInteriorPdfSequential(
        FileReference target,
        IReadOnlyList<FileReference> introPages,
        IReadOnlyList<FileReference> orderedInteriorPages,
        FileReference? backgroundPage,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        using var document = new PdfDocument();

        foreach (var intro in introPages)
        {
            AddRasterPage(document, intro, pageSize, cancellationToken);
            if (backgroundPage is not null)
            {
                AddRasterPage(document, backgroundPage, pageSize, cancellationToken);
            }
        }

        foreach (var artwork in orderedInteriorPages)
        {
            AddRasterPage(document, artwork, pageSize, cancellationToken);
            if (backgroundPage is not null)
            {
                AddRasterPage(document, backgroundPage, pageSize, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        document.Save(target.Value);
    }

    private static void AddRasterPage(
        PdfDocument document,
        FileReference source,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageSize.WidthInPoints);
        page.Height = XUnit.FromPoint(pageSize.HeightInPoints);

        using var stream = File.OpenRead(source.Value);
        using var image = XImage.FromStream(stream);
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawImage(image, 0, 0, pageSize.WidthInPoints, pageSize.HeightInPoints);
    }
}
