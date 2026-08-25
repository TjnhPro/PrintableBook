using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public sealed record AssetPreviewRequest(string BookId, string SourceReference);

public sealed class AssetPreviewWorker(IBookAssetPreviewService previewService) : BackgroundTaskWorker<AssetPreviewRequest, BookAssetPreview?>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.AssetPreview;

    protected override async ValueTask<BookAssetPreview?> ExecuteTypedAsync(AssetPreviewRequest request, IBackgroundTaskContext context, CancellationToken cancellationToken)
    {
        context.Report("preview.generate", subject: $"{request.BookId}/{Path.GetFileName(request.SourceReference)}");
        return await previewService.GetAsync(request.BookId, request.SourceReference, cancellationToken);
    }
}
