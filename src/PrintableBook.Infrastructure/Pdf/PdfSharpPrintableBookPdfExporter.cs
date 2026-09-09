using System.Collections.Concurrent;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Pdf;

public sealed class PdfSharpPrintableBookPdfExporter : IPrintableBookPdfExporter
{
    public async ValueTask<PrintableBookPdfExportResult> ExportAsync(
        PrintableBookPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request, cancellationToken);
        Directory.CreateDirectory(request.TemporaryOutputDirectory.Value);

        var coverPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "cover.pdf"));
        var interiorPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf"));
        WriteSingleRasterPdf(coverPdf, request.Cover, request.CoverPageSize, cancellationToken);
        await WriteInteriorPdfAsync(
            interiorPdf,
            request.IntroPages,
            request.OrderedInteriorPages,
            request.BackgroundPage,
            request.InteriorPageSize,
            request.MaximumPageConcurrency,
            cancellationToken);

        return new PrintableBookPdfExportResult(coverPdf, interiorPdf);
    }

    public async ValueTask<InteriorPdfExportResult> ExportInteriorAsync(
        InteriorPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request, cancellationToken);
        Directory.CreateDirectory(request.TemporaryOutputDirectory.Value);

        var interiorPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf"));
        await WriteInteriorPdfAsync(
            interiorPdf,
            request.IntroPages,
            request.OrderedInteriorPages,
            request.BackgroundPage,
            request.InteriorPageSize,
            request.MaximumPageConcurrency,
            cancellationToken);

        return new InteriorPdfExportResult(interiorPdf);
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

    private static async ValueTask WriteInteriorPdfAsync(
        FileReference target,
        IReadOnlyList<FileReference> introPages,
        IReadOnlyList<FileReference> orderedInteriorPages,
        FileReference? backgroundPage,
        PhysicalPageSize pageSize,
        int maximumPageConcurrency,
        CancellationToken cancellationToken)
    {
        var importedInteriors = new ConcurrentDictionary<int, PdfDocument>();
        PdfDocument? introImport = null;
        using var semaphore = new SemaphoreSlim(maximumPageConcurrency, maximumPageConcurrency);
        using var remainingWorkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task[] importTasks = [];

        try
        {
            if (introPages.Count > 0)
            {
                introImport = CreateImportDocument(
                    BuildIntroRasterPages(introPages, backgroundPage),
                    pageSize,
                    cancellationToken);
            }

            importTasks = orderedInteriorPages
                .Select((artwork, index) =>
                    Task.Run(
                        () => ImportInteriorAsync(
                            index,
                            artwork,
                            backgroundPage,
                            pageSize,
                            importedInteriors,
                            semaphore,
                            remainingWorkCancellation),
                        CancellationToken.None))
                .ToArray();

            try
            {
                await Task.WhenAll(importTasks);
            }
            catch
            {
                await remainingWorkCancellation.CancelAsync();

                try
                {
                    await Task.WhenAll(importTasks);
                }
                catch
                {
                    // Preserve the original worker failure.
                }

                throw;
            }

            using var finalDocument = new PdfDocument();
            if (introImport is not null)
            {
                finalDocument.Pages.InsertRange(finalDocument.Pages.Count, introImport);
                CloseAndDispose(introImport);
                introImport = null;
            }

            foreach (var index in importedInteriors.Keys.Order())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!importedInteriors.TryRemove(index, out var imported))
                {
                    throw new InvalidOperationException($"Interior import index {index} is missing.");
                }

                try
                {
                    finalDocument.Pages.InsertRange(finalDocument.Pages.Count, imported);
                }
                finally
                {
                    CloseAndDispose(imported);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            finalDocument.Save(target.Value);
        }
        finally
        {
            if (introImport is not null)
            {
                CloseAndDispose(introImport);
            }

            foreach (var pair in importedInteriors)
            {
                if (importedInteriors.TryRemove(pair.Key, out var imported))
                {
                    CloseAndDispose(imported);
                }
            }
        }
    }

    private static IReadOnlyList<FileReference> BuildIntroRasterPages(
        IReadOnlyList<FileReference> introPages,
        FileReference? backgroundPage)
    {
        var result = new List<FileReference>(introPages.Count * (backgroundPage is null ? 1 : 2));
        foreach (var intro in introPages)
        {
            result.Add(intro);
            if (backgroundPage is not null)
            {
                result.Add(backgroundPage);
            }
        }

        return result;
    }

    private static PdfDocument CreateImportDocument(
        IReadOnlyList<FileReference> rasterPages,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var staging = new PdfDocument())
        {
            foreach (var source in rasterPages)
            {
                AddRasterPage(staging, source, pageSize, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            staging.Save(buffer, closeStream: false);
        }

        buffer.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();
        return PdfReader.Open(buffer, PdfDocumentOpenMode.Import);
    }

    private static async Task ImportInteriorAsync(
        int index,
        FileReference artwork,
        FileReference? backgroundPage,
        PhysicalPageSize pageSize,
        ConcurrentDictionary<int, PdfDocument> importedInteriors,
        SemaphoreSlim semaphore,
        CancellationTokenSource remainingWorkCancellation)
    {
        var enteredSemaphore = false;
        PdfDocument? imported = null;

        try
        {
            await semaphore.WaitAsync(remainingWorkCancellation.Token);
            enteredSemaphore = true;
            var pages = backgroundPage is null ? [artwork] : new[] { artwork, backgroundPage };
            imported = CreateImportDocument(pages, pageSize, remainingWorkCancellation.Token);
            if (!importedInteriors.TryAdd(index, imported))
            {
                throw new InvalidOperationException($"Interior import index {index} already exists.");
            }

            imported = null;
        }
        catch
        {
            if (imported is not null)
            {
                CloseAndDispose(imported);
            }

            await remainingWorkCancellation.CancelAsync();
            throw;
        }
        finally
        {
            if (enteredSemaphore)
            {
                semaphore.Release();
            }
        }
    }

    private static void CloseAndDispose(PdfDocument document)
    {
        try
        {
            document.Close();
        }
        finally
        {
            document.Dispose();
        }
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
