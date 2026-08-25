using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Application.Results;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application.BackgroundTasks;

public sealed class ProcessingSessionWorkerTests
{
    [Fact]
    public async Task Execution_gets_a_fresh_snapshot_then_reports_an_immutable_terminal_view()
    {
        var provider = new Provider(Snapshot());
        var application = new Application();
        var frame = new FrameResolver();
        IBackgroundTaskWorker worker = new ProcessingSessionWorker(provider, application, frame);
        var context = new Context();

        await worker.ExecuteAsync(Request(), context, CancellationToken.None);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, frame.Calls);
        Assert.NotNull(application.Request);
        Assert.False(context.View!.IsActive);
        Assert.Equal("Completed", context.View.CurrentStep);
    }

    [Fact]
    public async Task Validation_failure_uses_a_safe_code_and_terminal_view_before_resolving_a_frame()
    {
        var context = new Context();
        IBackgroundTaskWorker worker = new ProcessingSessionWorker(new Provider(Snapshot() with { Discovery = Snapshot().Discovery with { Brands = [] } }), new Application(), new FrameResolver());

        var failure = await Assert.ThrowsAsync<BackgroundTaskFailureException>(() => worker.ExecuteAsync(Request(), context, CancellationToken.None).AsTask());

        Assert.Equal("process_brand_not_found", failure.Code);
        Assert.False(context.View!.IsActive);
        Assert.Equal("Failed", context.View.CurrentStep);
    }

    private static ProcessingSessionWorkerRequest Request() => new(["book-one"], "Brand", BookProcessingMode.InteriorOnly, DateTimeOffset.UtcNow);

    private static ApplicationSnapshot Snapshot()
    {
        var bookId = new BookId("book-one");
        var workspace = new BookWorkspace(bookId, new DirectoryReference("workspace"), new DirectoryReference("processed"), new DirectoryReference("temp"));
        var book = new DiscoveredBook("Book One", bookId, new DirectoryReference("book-one"), workspace);
        return new ApplicationSnapshot(
            new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [new DiscoveredBrand("Brand", new DirectoryReference("brand"))], [book]),
            GlobalSettings.Default,
            [new BookDesktopSummary(bookId, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 1)],
            DateTimeOffset.UtcNow);
    }

    private sealed class Context : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("process-test");
        public ProcessSessionSnapshot? View { get; private set; }
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class => View = view as ProcessSessionSnapshot;
    }

    private sealed class Provider(ApplicationSnapshot snapshot) : IApplicationSnapshotProvider
    {
        public int Calls { get; private set; }
        public ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default) { Calls++; return ValueTask.FromResult(snapshot); }
    }

    private sealed class FrameResolver : IBrandFrameResolver
    {
        public int Calls { get; private set; }
        public ValueTask<FileReference?> ResolveCompatibleFrameAsync(DiscoveredBrand brand, ImageSize targetSize, CancellationToken cancellationToken = default) { Calls++; return ValueTask.FromResult<FileReference?>(null); }
    }

    private sealed class Application : IPrintableBookApplication
    {
        public BookProcessingQueueRequest? Request { get; private set; }
        public ValueTask<ProcessingResult> ProcessAsync(PrintableBook.Core.Application.Commands.ProcessingRequest request, IProgress<PrintableBook.Core.Application.Progress.ProcessingProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BookProcessingQueueResult> ProcessBooksAsync(BookProcessingQueueRequest request, Action<BookProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Request = request;
            progress?.Invoke(new BookProcessingProgress(request.Books[0].BookId, BookProcessingStatus.Running, "interior-pages", 1, 1));
            return ValueTask.FromResult(new BookProcessingQueueResult(false, [BookProcessingQueueBookResult.CompletedInterior(request.Books[0].BookId, new PublishedInteriorOutput(new DirectoryReference("output"), new FileReference("interior.pdf")))]));
        }
    }
}
