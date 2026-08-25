using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Tests.Application.BackgroundTasks;

public sealed class AssetPreviewWorkerTests
{
    [Fact]
    public async Task Execution_reports_the_preview_step_and_returns_the_service_result()
    {
        var preview = new BookAssetPreview("Book One", "Book interior/page-001.png", 100, 120, "data:image/png;base64,test");
        var service = new PreviewService(preview);
        IBackgroundTaskWorker worker = new AssetPreviewWorker(service);
        var context = new Context();

        var result = await worker.ExecuteAsync(new AssetPreviewRequest("Book One", "Book interior/page-001.png"), context, CancellationToken.None);

        Assert.Same(preview, result);
        Assert.Equal(("Book One", "Book interior/page-001.png"), service.LastRequest);
        Assert.Equal(("preview.generate", "Book One/page-001.png"), context.Reported);
    }

    private sealed class Context : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("preview-test");
        public (string Step, string? Subject)? Reported { get; private set; }
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) => Reported = (step, subject);
        public void SetView<TView>(TView view) where TView : class { }
    }

    private sealed class PreviewService(BookAssetPreview preview) : IBookAssetPreviewService
    {
        public (string BookId, string SourceReference)? LastRequest { get; private set; }
        public ValueTask<BookAssetPreview?> GetAsync(string bookId, string sourceReference, CancellationToken cancellationToken = default)
        {
            LastRequest = (bookId, sourceReference);
            return ValueTask.FromResult<BookAssetPreview?>(preview);
        }
    }
}
