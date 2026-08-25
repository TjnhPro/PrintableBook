using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Diagnostics;

namespace PrintableBook.Desktop.Preview;

public sealed class BookAssetPreviewCoordinator(
    IBookAssetPreviewService previewService,
    IOperationDiagnostics diagnostics)
{
    private readonly SemaphoreSlim slots = new(2, 2);

    public async ValueTask<BookAssetPreview?> GetAsync(string bookId, string sourceReference, CancellationToken cancellationToken = default)
    {
        await slots.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(async () =>
            {
                using var operation = diagnostics.Begin("preview.generate", $"{bookId}/{Path.GetFileName(sourceReference)}");
                return await previewService.GetAsync(bookId, sourceReference, cancellationToken);
            }, CancellationToken.None);
        }
        finally
        {
            slots.Release();
        }
    }
}
