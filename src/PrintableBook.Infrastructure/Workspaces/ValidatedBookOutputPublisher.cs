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
        var coverPreviewPdf = new FileReference(Path.Combine(publishedDirectory.Value, $"{request.BookId.Value} - Cover_thumbnail.pdf"));
        var coverThumbnailImage = new FileReference(Path.Combine(publishedDirectory.Value, $"{request.BookId.Value} - Cover_thumbnail.png"));
        var interiorPreviewPdf = new FileReference(Path.Combine(publishedDirectory.Value, $"{request.BookId.Value} - Interior_thumbnail.pdf"));
        var validatedCoverPreview = await TryValidatePreviewAsync(
            request.TemporaryOutput.CoverPreviewPdf,
            request.TemporaryOutput.CoverPdf,
            request.Validation.ExpectedCoverPageCount,
            request.Validation.ExpectedCoverPageSize,
            cancellationToken);
        var validatedInteriorPreview = await TryValidatePreviewAsync(
            request.TemporaryOutput.InteriorPreviewPdf,
            request.TemporaryOutput.InteriorPdf,
            request.Validation.ExpectedInteriorPageCount,
            request.Validation.ExpectedInteriorPageSize,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        ReplaceFile(request.TemporaryOutput.CoverPdf, coverPdf);
        ReplaceFile(request.TemporaryOutput.InteriorPdf, interiorPdf);
        var publishedCoverPreview = TryPublishValidatedPreview(validatedCoverPreview, coverPreviewPdf);
        TryPublishCoverThumbnailImage(request.TemporaryOutput.CoverPdf, coverThumbnailImage);
        var publishedInteriorPreview = TryPublishValidatedPreview(validatedInteriorPreview, interiorPreviewPdf);
        DeleteTemporaryDirectory(request.TemporaryOutput.CoverPdf);

        return new PublishedBookOutputs(
            publishedDirectory,
            coverPdf,
            interiorPdf,
            publishedCoverPreview,
            publishedInteriorPreview);
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
        var previewPdf = new FileReference(Path.Combine(publishedDirectory.Value, $"{request.BookId.Value} - Interior_thumbnail.pdf"));
        var validatedPreview = await TryValidatePreviewAsync(
            request.TemporaryOutput.PreviewPdf,
            request.TemporaryOutput.InteriorPdf,
            request.ExpectedInteriorPageCount,
            request.ExpectedInteriorPageSize,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        ReplaceFile(request.TemporaryOutput.InteriorPdf, interiorPdf);
        var publishedPreview = TryPublishValidatedPreview(validatedPreview, previewPdf);
        DeleteTemporaryDirectory(request.TemporaryOutput.InteriorPdf);

        return new PublishedInteriorOutput(
            publishedDirectory,
            interiorPdf,
            publishedPreview);
    }

    public async ValueTask<PublishedCoverOutput> PublishCoverAsync(
        CoverOutputPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ValidateAsync(request.TemporaryOutput.CoverPdf, request.ExpectedCoverPageCount, request.ExpectedCoverPageSize, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(request.FinalOutputRoot.Value);
        var coverPdf = new FileReference(Path.Combine(request.FinalOutputRoot.Value, $"{request.BookId.Value} - Cover.pdf"));
        var previewPdf = new FileReference(Path.Combine(request.FinalOutputRoot.Value, $"{request.BookId.Value} - Cover_thumbnail.pdf"));
        var thumbnailImage = new FileReference(Path.Combine(request.FinalOutputRoot.Value, $"{request.BookId.Value} - Cover_thumbnail.png"));
        var validatedPreview = await TryValidatePreviewAsync(
            request.TemporaryOutput.PreviewPdf,
            request.TemporaryOutput.CoverPdf,
            request.ExpectedCoverPageCount,
            request.ExpectedCoverPageSize,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        ReplaceFile(request.TemporaryOutput.CoverPdf, coverPdf);
        var publishedPreview = TryPublishValidatedPreview(validatedPreview, previewPdf);
        TryPublishCoverThumbnailImage(request.TemporaryOutput.CoverPdf, thumbnailImage);
        DeleteTemporaryDirectory(request.TemporaryOutput.CoverPdf);
        return new PublishedCoverOutput(request.FinalOutputRoot, coverPdf, publishedPreview);
    }

    private async ValueTask<FileReference?> TryValidatePreviewAsync(
        FileReference? temporaryPreview,
        FileReference temporaryMain,
        int expectedPageCount,
        PhysicalPageSize expectedPageSize,
        CancellationToken cancellationToken)
    {
        if (temporaryPreview is null || !File.Exists(temporaryPreview.Value)) return null;

        try
        {
            await ValidateAsync(temporaryPreview, expectedPageCount, expectedPageSize, cancellationToken);
            if (new FileInfo(temporaryPreview.Value).Length >= new FileInfo(temporaryMain.Value).Length)
            {
                return null;
            }

            return temporaryPreview;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileReference? TryPublishValidatedPreview(FileReference? temporaryPreview, FileReference finalPreview)
    {
        if (temporaryPreview is null) return null;

        try
        {
            ReplaceFile(temporaryPreview, finalPreview);
            return finalPreview;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryPublishCoverThumbnailImage(FileReference temporaryCoverPdf, FileReference finalThumbnail)
    {
        var temporaryDirectory = Path.GetDirectoryName(temporaryCoverPdf.Value);
        var temporaryThumbnail = temporaryDirectory is null
            ? null
            : new FileReference(Path.Combine(temporaryDirectory, "cover_thumbnail.png"));
        try
        {
            if (temporaryThumbnail is null || !File.Exists(temporaryThumbnail.Value))
            {
                DeleteStaleThumbnail(finalThumbnail);
                return;
            }

            ReplaceFile(temporaryThumbnail, finalThumbnail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteStaleThumbnail(finalThumbnail);
        }
    }

    private static void DeleteStaleThumbnail(FileReference thumbnail)
    {
        try
        {
            if (File.Exists(thumbnail.Value)) File.Delete(thumbnail.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The snapshot freshness check prevents an older image from representing a newer Cover PDF.
        }
    }

    private static void ReplaceFile(FileReference temporaryFile, FileReference finalFile)
    {
        var pending = $"{finalFile.Value}.pending";
        try
        {
            File.Move(temporaryFile.Value, pending, overwrite: true);
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
