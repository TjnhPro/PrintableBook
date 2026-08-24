using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Commands;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Progress;
using PrintableBook.Core.Application.Results;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using System.Reflection;

namespace PrintableBook.Core.Tests.Application;

public sealed class ProcessSessionServiceTests
{
    [Fact]
    public async Task StartAsync_runs_processing_outside_the_caller_synchronization_context()
    {
        var prior = SynchronizationContext.Current;
        var marker = new MarkerSynchronizationContext();
        var application = new RecordingPrintableBookApplication();

        try
        {
            SynchronizationContext.SetSynchronizationContext(marker);
            var service = new ProcessSessionService(
                new StaticSnapshotService(CreateSnapshot()),
                application,
                new NullBrandFrameResolver());

            var started = await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
            await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(started.IsActive);
            Assert.NotSame(marker, application.ExecutionContext);
        }
        finally
        {
            application.Release.TrySetResult();
            SynchronizationContext.SetSynchronizationContext(prior);
        }
    }

    [Fact]
    public async Task StartAsync_returns_the_existing_active_session_without_starting_a_second_worker()
    {
        var application = new RecordingPrintableBookApplication();
        var service = new ProcessSessionService(
            new StaticSnapshotService(CreateSnapshot()),
            application,
            new NullBrandFrameResolver());

        var first = await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);

        Assert.True(first.IsActive);
        Assert.True(second.IsActive);
        Assert.Equal(first.CurrentBookId, second.CurrentBookId);
        Assert.Equal(1, application.InvocationCount);

        application.Release.TrySetResult();
        await WaitUntilAsync(async () => !(await service.GetAsync()).IsActive);
        Assert.False((await service.GetAsync()).IsActive);
    }

    [Fact]
    public async Task CancelAsync_marks_the_session_as_cancelling_before_the_worker_unwinds()
    {
        var application = new RecordingPrintableBookApplication();
        var service = new ProcessSessionService(
            new StaticSnapshotService(CreateSnapshot()),
            application,
            new NullBrandFrameResolver());
        await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var cancelling = await service.CancelAsync();

        Assert.True(cancelling.IsActive);
        Assert.True(cancelling.IsCancelling);
        Assert.Equal("Cancelling", cancelling.CurrentStep);
        Assert.True(application.LastCancellationToken.IsCancellationRequested);
        await WaitUntilAsync(async () => !(await service.GetAsync()).IsActive);
        Assert.Equal("Cancelled", (await service.GetAsync()).CurrentStep);
    }

    [Fact]
    public async Task StopAndWaitAsync_returns_false_for_a_non_cooperative_worker_then_can_complete_later()
    {
        var application = new RecordingPrintableBookApplication(observesCancellation: false);
        var service = new ProcessSessionService(
            new StaticSnapshotService(CreateSnapshot()),
            application,
            new NullBrandFrameResolver());
        await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(await service.StopAndWaitAsync(TimeSpan.FromMilliseconds(20)));

        application.Release.TrySetResult();
        Assert.True(await service.StopAndWaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task CancelAsync_does_not_fail_when_worker_reaches_terminal_cleanup_concurrently()
    {
        var application = new RecordingPrintableBookApplication(observesCancellation: false);
        var service = new ProcessSessionService(
            new StaticSnapshotService(CreateSnapshot()),
            application,
            new NullBrandFrameResolver());
        await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        DisposeOwnedCancellation(service);

        var cancelling = await service.CancelAsync();

        Assert.True(cancelling.IsCancelling);
        application.Release.TrySetResult();
        await WaitUntilAsync(async () => !(await service.GetAsync()).IsActive);
    }

    [Fact]
    public async Task StopAndWaitAsync_does_not_fail_when_worker_finishes_during_cancellation_request()
    {
        var application = new RecordingPrintableBookApplication(observesCancellation: false);
        var service = new ProcessSessionService(
            new StaticSnapshotService(CreateSnapshot()),
            application,
            new NullBrandFrameResolver());
        await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        DisposeOwnedCancellation(service);
        application.Release.TrySetResult();

        Assert.True(await service.StopAndWaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task StartAsync_allows_a_new_session_after_terminal_cleanup()
    {
        var application = new RecordingPrintableBookApplication();
        var service = new ProcessSessionService(
            new StaticSnapshotService(CreateSnapshot()),
            application,
            new NullBrandFrameResolver());
        await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        application.Release.TrySetResult();
        await WaitUntilAsync(async () => !(await service.GetAsync()).IsActive);

        var restarted = await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);

        Assert.True(restarted.IsActive);
        await WaitUntilAsync(() => ValueTask.FromResult(application.InvocationCount == 2));
    }

    [Fact]
    public async Task GetAsync_refreshes_the_current_book_and_queue_for_a_multi_book_session()
    {
        var application = new RecordingPrintableBookApplication();
        var service = new ProcessSessionService(
            new StaticSnapshotService(CreateSnapshot(
                ("book-one", BookProcessingStatus.Completed, "Completed"),
                ("book-two", BookProcessingStatus.Running, "Interior processing"))),
            application,
            new NullBrandFrameResolver());
        await service.StartAsync(["book-one", "book-two"], "Brand", BookProcessingMode.InteriorOnly);
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var refreshed = await service.GetAsync();

        Assert.Equal(new BookId("book-two"), refreshed.CurrentBookId);
        Assert.Equal("Interior processing", refreshed.CurrentStep);
        Assert.Equal(BookProcessingStatus.Completed, refreshed.Queue.Single(entry => entry.BookId == new BookId("book-one")).Status);
        Assert.Equal(BookProcessingStatus.Running, refreshed.Queue.Single(entry => entry.BookId == new BookId("book-two")).Status);
        application.Release.TrySetResult();
    }

    private static ApplicationSnapshot CreateSnapshot(params (string Id, BookProcessingStatus Status, string? CurrentStep)[] states)
    {
        if (states.Length == 0) states = [("book-one", BookProcessingStatus.NotStarted, null)];
        var root = new DirectoryReference("C:\\test-root");
        var books = states.Select(state =>
        {
            var bookId = new BookId(state.Id);
            var workspace = new BookWorkspace(
                bookId,
                new DirectoryReference($"C:\\test-root\\{state.Id}\\.workspace"),
                new DirectoryReference($"C:\\test-root\\{state.Id}\\.workspace\\processed"),
                new DirectoryReference($"C:\\test-root\\{state.Id}\\.workspace\\output-temp"));
            return new DiscoveredBook(state.Id, bookId, new DirectoryReference($"C:\\test-root\\{state.Id}"), workspace);
        }).ToArray();
        return new ApplicationSnapshot(
            new ApplicationDiscovery(
                new ApplicationPaths(root, new DirectoryReference("C:\\test-root\\brands"), new DirectoryReference("C:\\test-root\\sources"), new FileReference("C:\\test-root\\settings.json")),
                [new DiscoveredBrand("Brand", new DirectoryReference("C:\\test-root\\brands\\Brand"))],
                books),
            GlobalSettings.Default,
            states.Select(state => new BookDesktopSummary(
                new BookId(state.Id),
                "Ready",
                [],
                state.Status,
                state.CurrentStep,
                null,
                [],
                state.Status == BookProcessingStatus.Running ? [new InteriorPageSummary("page-01", "Processing", "final.png")] : [],
                [],
                1)).ToArray(),
            DateTimeOffset.UtcNow);
    }

    private sealed class MarkerSynchronizationContext : SynchronizationContext;

    private static void DisposeOwnedCancellation(ProcessSessionService service)
    {
        var field = typeof(ProcessSessionService).GetField("cancellation", BindingFlags.Instance | BindingFlags.NonPublic);
        var source = Assert.IsType<CancellationTokenSource>(field?.GetValue(service));
        source.Dispose();
    }

    private sealed class StaticSnapshotService(ApplicationSnapshot snapshot) : IApplicationSnapshotService
    {
        public ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

    private sealed class NullBrandFrameResolver : IBrandFrameResolver
    {
        public ValueTask<FileReference?> ResolveCompatibleFrameAsync(DiscoveredBrand brand, ImageSize targetSize, CancellationToken cancellationToken = default) => ValueTask.FromResult<FileReference?>(null);
    }

    private sealed class RecordingPrintableBookApplication(bool observesCancellation = true) : IPrintableBookApplication
    {
        public SynchronizationContext? ExecutionContext { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public int InvocationCount { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ProcessingResult> ProcessAsync(ProcessingRequest request, IProgress<ProcessingProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async ValueTask<BookProcessingQueueResult> ProcessBooksAsync(BookProcessingQueueRequest request, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            ExecutionContext = SynchronizationContext.Current;
            LastCancellationToken = cancellationToken;
            Started.TrySetResult();
            await Release.Task.WaitAsync(observesCancellation ? cancellationToken : CancellationToken.None);
            return new BookProcessingQueueResult(false, request.Books.Select(book => BookProcessingQueueBookResult.Completed(book.BookId, null)).ToArray());
        }
    }

    private static async Task WaitUntilAsync(Func<ValueTask<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The process session did not reach its expected state.");
            await Task.Delay(10);
        }
    }
}
