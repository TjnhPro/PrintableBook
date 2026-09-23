using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;

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
    BookId BookId,
    PrintableBookPdfExportResult TemporaryOutput,
    DirectoryReference FinalOutputRoot,
    PrintableBookPdfValidation Validation);

public sealed record PublishedBookOutputs(
    DirectoryReference PublishedDirectory,
    FileReference CoverPdf,
    FileReference InteriorPdf,
    FileReference? CoverPreviewPdf = null,
    FileReference? InteriorPreviewPdf = null);

public sealed record InteriorOutputPublicationRequest(
    BookId BookId,
    InteriorPdfExportResult TemporaryOutput,
    DirectoryReference FinalOutputRoot,
    int ExpectedInteriorPageCount,
    PhysicalPageSize ExpectedInteriorPageSize);

public sealed record PublishedInteriorOutput(
    DirectoryReference PublishedDirectory,
    FileReference InteriorPdf,
    FileReference? PreviewPdf = null);

public sealed record CoverOutputPublicationRequest(
    BookId BookId,
    CoverPdfExportResult TemporaryOutput,
    DirectoryReference FinalOutputRoot,
    int ExpectedCoverPageCount,
    PhysicalPageSize ExpectedCoverPageSize);

public sealed record PublishedCoverOutput(
    DirectoryReference PublishedDirectory,
    FileReference CoverPdf,
    FileReference? PreviewPdf = null);

public interface IBookOutputPublisher
{
    ValueTask<PublishedBookOutputs> PublishAsync(
        BookOutputPublicationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PublishedInteriorOutput> PublishInteriorAsync(
        InteriorOutputPublicationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PublishedCoverOutput> PublishCoverAsync(
        CoverOutputPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Cover-only publication is not supported by this adapter.");
}
