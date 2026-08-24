using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Workspaces;

/// <summary>
/// Publishes a complete, validated pair of PDFs as one versioned directory.
/// </summary>
public sealed class ValidatedBookOutputPublisher(IPdfDocumentInspector pdfDocumentInspector) : IBookOutputPublisher
{
    public async ValueTask<PublishedBookOutputs> PublishAsync(
        BookOutputPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var temporaryDirectoryPath = Path.GetDirectoryName(request.TemporaryOutput.CoverPdf.Value)
            ?? throw new ArgumentException("Temporary cover output must have a parent directory.", nameof(request));
        var interiorDirectoryPath = Path.GetDirectoryName(request.TemporaryOutput.InteriorPdf.Value);
        if (!string.Equals(temporaryDirectoryPath, interiorDirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Temporary cover and interior PDFs must be in the same directory.", nameof(request));
        }

        await ValidateAsync(request.TemporaryOutput.CoverPdf, request.Validation.ExpectedCoverPageCount, request.Validation.ExpectedCoverPageSize, cancellationToken);
        await ValidateAsync(request.TemporaryOutput.InteriorPdf, request.Validation.ExpectedInteriorPageCount, request.Validation.ExpectedInteriorPageSize, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(request.FinalOutputRoot.Value);
        var publishedDirectory = new DirectoryReference(Path.Combine(
            request.FinalOutputRoot.Value,
            $"run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"));
        Directory.Move(temporaryDirectoryPath, publishedDirectory.Value);

        return new PublishedBookOutputs(
            publishedDirectory,
            new FileReference(Path.Combine(publishedDirectory.Value, Path.GetFileName(request.TemporaryOutput.CoverPdf.Value))),
            new FileReference(Path.Combine(publishedDirectory.Value, Path.GetFileName(request.TemporaryOutput.InteriorPdf.Value))));
    }

    public async ValueTask<PublishedInteriorOutput> PublishInteriorAsync(
        InteriorOutputPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var temporaryDirectoryPath = Path.GetDirectoryName(request.TemporaryOutput.InteriorPdf.Value)
            ?? throw new ArgumentException("Temporary interior output must have a parent directory.", nameof(request));
        await ValidateAsync(request.TemporaryOutput.InteriorPdf, request.ExpectedInteriorPageCount, request.ExpectedInteriorPageSize, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(request.FinalOutputRoot.Value);
        var publishedDirectory = new DirectoryReference(Path.Combine(
            request.FinalOutputRoot.Value,
            $"run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"));
        Directory.Move(temporaryDirectoryPath, publishedDirectory.Value);

        return new PublishedInteriorOutput(
            publishedDirectory,
            new FileReference(Path.Combine(publishedDirectory.Value, Path.GetFileName(request.TemporaryOutput.InteriorPdf.Value))));
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
