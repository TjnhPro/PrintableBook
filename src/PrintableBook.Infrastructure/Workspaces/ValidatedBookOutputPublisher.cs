using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Workspaces;

/// <summary>
/// Publishes validated PDFs as the current Book-local output files.
/// </summary>
public sealed class ValidatedBookOutputPublisher(IPdfDocumentInspector pdfDocumentInspector) : IBookOutputPublisher
{
    public async ValueTask<PublishedBookOutputs> PublishAsync(
        BookOutputPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ValidateAsync(request.TemporaryOutput.CoverPdf, request.Validation.ExpectedCoverPageCount, request.Validation.ExpectedCoverPageSize, cancellationToken);
        await ValidateAsync(request.TemporaryOutput.InteriorPdf, request.Validation.ExpectedInteriorPageCount, request.Validation.ExpectedInteriorPageSize, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(request.FinalOutputRoot.Value);
        var publishedDirectory = request.FinalOutputRoot;
        var coverPdf = new FileReference(Path.Combine(publishedDirectory.Value, $"{request.BookId.Value} - Cover.pdf"));
        var interiorPdf = new FileReference(Path.Combine(publishedDirectory.Value, $"{request.BookId.Value} - Interior.pdf"));
        ReplaceFile(request.TemporaryOutput.CoverPdf, coverPdf);
        ReplaceFile(request.TemporaryOutput.InteriorPdf, interiorPdf);
        DeleteTemporaryDirectory(request.TemporaryOutput.CoverPdf);

        return new PublishedBookOutputs(
            publishedDirectory,
            coverPdf,
            interiorPdf);
    }

    public async ValueTask<PublishedInteriorOutput> PublishInteriorAsync(
        InteriorOutputPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ValidateAsync(request.TemporaryOutput.InteriorPdf, request.ExpectedInteriorPageCount, request.ExpectedInteriorPageSize, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(request.FinalOutputRoot.Value);
        var publishedDirectory = request.FinalOutputRoot;
        var interiorPdf = new FileReference(Path.Combine(publishedDirectory.Value, $"{request.BookId.Value} - Interior.pdf"));
        ReplaceFile(request.TemporaryOutput.InteriorPdf, interiorPdf);
        DeleteTemporaryDirectory(request.TemporaryOutput.InteriorPdf);

        return new PublishedInteriorOutput(
            publishedDirectory,
            interiorPdf);
    }

    private static void ReplaceFile(FileReference temporaryFile, FileReference finalFile)
    {
        var pending = $"{finalFile.Value}.{Guid.NewGuid():N}.pending";
        try
        {
            File.Copy(temporaryFile.Value, pending, overwrite: true);
            File.Move(pending, finalFile.Value, overwrite: true);
        }
        finally
        {
            if (File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    private static void DeleteTemporaryDirectory(FileReference temporaryFile)
    {
        var temporaryDirectory = Path.GetDirectoryName(temporaryFile.Value)
            ?? throw new InvalidOperationException("Temporary PDF output must have a parent directory.");
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private async ValueTask ValidateAsync(
        FileReference pdf,
        int expectedPageCount,
        PhysicalPageSize expectedSize,
        CancellationToken cancellationToken)
    {
        var inspection = await pdfDocumentInspector.InspectAsync(pdf, cancellationToken);
        if (inspection.PageCount != expectedPageCount ||
            Math.Abs(inspection.FirstPageSize.WidthInches - expectedSize.WidthInches) > 0.001 ||
            Math.Abs(inspection.FirstPageSize.HeightInches - expectedSize.HeightInches) > 0.001)
        {
            throw new InvalidDataException($"PDF '{pdf.Value}' did not pass publication validation.");
        }
    }
}
