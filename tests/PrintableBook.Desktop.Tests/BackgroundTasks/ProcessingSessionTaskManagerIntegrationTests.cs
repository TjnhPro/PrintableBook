using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Desktop.BackgroundTasks;

namespace PrintableBook.Desktop.Tests.BackgroundTasks;

public sealed class ProcessingSessionTaskManagerIntegrationTests
{
    [Fact]
    public async Task Requested_cancellation_preserves_mixed_book_results_and_ends_the_manager_task_as_cancelled()
    {
        var application = new CancellationResultApplication();
        var services = new ServiceCollection()
            .AddKeyedSingleton<IBackgroundTaskWorker>(BackgroundTaskKind.LibraryRefresh, new UnusedWorker(BackgroundTaskKind.LibraryRefresh))
            .AddKeyedSingleton<IBackgroundTaskWorker>(BackgroundTaskKind.ProcessingSession, new ProcessingSessionWorker(new SnapshotProvider(CreateSnapshot()), application, new NoFrameResolver()))
            .AddKeyedSingleton<IBackgroundTaskWorker>(BackgroundTaskKind.AssetPreview, new UnusedWorker(BackgroundTaskKind.AssetPreview))
            .BuildServiceProvider();
        using var manager = new BackgroundTaskManager(services, new NullDiagnostics());

        var task = await manager.StartAsync(BackgroundTaskKind.ProcessingSession, "processing", "book-one", new ProcessingSessionWorkerRequest(["book-one", "book-two"], "Brand", BookProcessingMode.InteriorOnly, DateTimeOffset.UtcNow));
        await application.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await manager.CancelAsync(task.TaskId);
        application.Release.TrySetResult();

        Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
        Assert.Equal(BackgroundTaskState.Cancelled, (await manager.GetAsync(task.TaskId))!.State);
        Assert.True(manager.TryGetView(task.TaskId, out ProcessSessionSnapshot? view));
        Assert.Equal("Cancelled", view!.CurrentStep);
        Assert.Collection(view.Queue,
            first => Assert.Equal(BookProcessingStatus.Completed, first.Status),
            second => Assert.Equal(BookProcessingStatus.Cancelled, second.Status));
    }

    private static ApplicationSnapshot CreateSnapshot()
    {
        var first = CreateBook("book-one");
        var second = CreateBook("book-two");
        return new ApplicationSnapshot(
            new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [new DiscoveredBrand("Brand", new DirectoryReference("brand"))], [first, second]),
            GlobalSettings.Default,
            [Summary(first.Id), Summary(second.Id)],
            DateTimeOffset.UtcNow);
    }

    private static DiscoveredBook CreateBook(string id)
    {
        var bookId = new BookId(id);
        return new DiscoveredBook(id, bookId, new DirectoryReference(id), new BookWorkspace(bookId, new DirectoryReference($"{id}/workspace"), new DirectoryReference($"{id}/processed"), new DirectoryReference($"{id}/temporary")));
    }

    private static BookDesktopSummary Summary(BookId id) => new(id, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 1);

    private sealed class SnapshotProvider(ApplicationSnapshot snapshot) : IApplicationSnapshotProvider
    {
        public ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

    private sealed class NoFrameResolver : IBrandFrameResolver
    {
        public ValueTask<FileReference?> ResolveCompatibleFrameAsync(DiscoveredBrand brand, ImageSize targetSize, CancellationToken cancellationToken = default) => ValueTask.FromResult<FileReference?>(null);
    }

    private sealed class CancellationResultApplication : IPrintableBookApplication
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<PrintableBook.Core.Application.Results.ProcessingResult> ProcessAsync(PrintableBook.Core.Application.Commands.ProcessingRequest request, IProgress<PrintableBook.Core.Application.Progress.ProcessingProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask<BookProcessingQueueResult> ProcessBooksAsync(BookProcessingQueueRequest request, Action<BookProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            return new BookProcessingQueueResult(false,
            [
                BookProcessingQueueBookResult.CompletedInterior(request.Books[0].BookId, new PublishedInteriorOutput(new DirectoryReference("output"), new FileReference("first.pdf"))),
                new BookProcessingQueueBookResult(request.Books[1].BookId, BookProcessingStatus.Cancelled, null, null)
            ]);
        }
    }

    private sealed class UnusedWorker(BackgroundTaskKind kind) : BackgroundTaskWorker<object, object>
    {
        public override BackgroundTaskKind Kind => kind;
        protected override ValueTask<object> ExecuteTypedAsync(object request, IBackgroundTaskContext context, CancellationToken cancellationToken) => ValueTask.FromResult(new object());
    }

    private sealed class NullDiagnostics : PrintableBook.Core.Application.Diagnostics.IOperationDiagnostics
    {
        public IDisposable Begin(string operation, string? subject = null) => new Scope();
        public void Record(string operation, string? subject = null, string? detail = null) { }
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
