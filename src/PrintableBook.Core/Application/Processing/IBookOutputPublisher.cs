using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record PdfDocumentInspection(int PageCount, PhysicalPageSize FirstPageSize);

public interface IPdfDocumentInspector
{
    ValueTask<PdfDocumentInspection> InspectAsync(FileReference pdf, CancellationToken cancellationToken = default);
}

public sealed record PrintableBookPdfValidation(
    int ExpectedCoverPageCount,
    int ExpectedInteriorPageCount,
    PhysicalPageSize ExpectedPageSize);

public sealed record BookOutputPublicationRequest(
    PrintableBookPdfExportResult TemporaryOutput,
    DirectoryReference FinalOutputRoot,
    PrintableBookPdfValidation Validation);

public sealed record PublishedBookOutputs(
    DirectoryReference PublishedDirectory,
    FileReference CoverPdf,
    FileReference InteriorPdf);

public interface IBookOutputPublisher
{
    ValueTask<PublishedBookOutputs> PublishAsync(
        BookOutputPublicationRequest request,
        CancellationToken cancellationToken = default);
}
