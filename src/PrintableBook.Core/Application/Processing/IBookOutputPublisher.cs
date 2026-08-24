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
    PhysicalPageSize ExpectedCoverPageSize,
    PhysicalPageSize ExpectedInteriorPageSize);

public sealed record BookOutputPublicationRequest(
    PrintableBookPdfExportResult TemporaryOutput,
    DirectoryReference FinalOutputRoot,
    PrintableBookPdfValidation Validation);

public sealed record PublishedBookOutputs(
    DirectoryReference PublishedDirectory,
    FileReference CoverPdf,
    FileReference InteriorPdf);

public sealed record InteriorOutputPublicationRequest(
    InteriorPdfExportResult TemporaryOutput,
    DirectoryReference FinalOutputRoot,
    int ExpectedInteriorPageCount,
    PhysicalPageSize ExpectedInteriorPageSize);

public sealed record PublishedInteriorOutput(
    DirectoryReference PublishedDirectory,
    FileReference InteriorPdf);

public interface IBookOutputPublisher
{
    ValueTask<PublishedBookOutputs> PublishAsync(
        BookOutputPublicationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PublishedInteriorOutput> PublishInteriorAsync(
        InteriorOutputPublicationRequest request,
        CancellationToken cancellationToken = default);
}
