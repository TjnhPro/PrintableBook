using PdfSharp.Pdf.IO;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Pdf;

public sealed class PdfSharpDocumentInspector : IPdfDocumentInspector
{
    public ValueTask<PdfDocumentInspection> InspectAsync(FileReference pdf, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfReader.Open(pdf.Value);
        if (document.Pages.Count == 0)
        {
            throw new InvalidDataException("A printable PDF must contain at least one page.");
        }

        var firstPage = document.Pages[0];
        return ValueTask.FromResult(new PdfDocumentInspection(
            document.Pages.Count,
            new PhysicalPageSize(firstPage.Width.Point / 72d, firstPage.Height.Point / 72d)));
    }
}
