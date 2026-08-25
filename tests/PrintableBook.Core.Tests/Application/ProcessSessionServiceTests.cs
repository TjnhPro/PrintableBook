using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application;

public sealed class ProcessSessionServiceTests
{
    [Fact]
    public async Task StartAsync_submits_one_immediate_processing_task_without_loading_a_snapshot()
    {
        var manager = new Manager();
        var service = new ProcessSessionService(manager);

        var started = await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);
        var duplicate = await service.StartAsync(["book-one"], "Brand", BookProcessingMode.InteriorOnly);

        Assert.Equal(1, manager.Starts);
        Assert.True(started.IsActive);
        Assert.Equal("Queued", started.CurrentStep);
        Assert.Equal(started.CurrentBookId, duplicate.CurrentBookId);
        Assert.IsType<ProcessingSessionWorkerRequest>(manager.Request);
    }

    [Fact]
    public async Task GetAsync_is_a_ram_only_view_and_overlays_cancelling_state()
    {
        var manager = new Manager { State = BackgroundTaskState.Cancelling };
        manager.SetView(new ProcessSessionSnapshot(true, false, "Brand", new BookId("book-one"), "Processing", [new ProcessQueueEntry(new BookId("book-one"), BookProcessingStatus.Running, "Processing")]));
        var service = new ProcessSessionService(manager);

        var view = await service.GetAsync();

        Assert.True(view.IsActive);
        Assert.True(view.IsCancelling);
        Assert.Equal("Cancelling", view.CurrentStep);
        Assert.Equal(1, manager.Lists);
    }

    [Fact]
    public async Task Repeated_get_reads_only_the_manager_ram_view()
    {
        var manager = new Manager { State = BackgroundTaskState.Running };
        manager.SetView(new ProcessSessionSnapshot(true, false, "Brand", new BookId("book-one"), "Processing", [new ProcessQueueEntry(new BookId("book-one"), BookProcessingStatus.Running, "Processing")]));
        var service = new ProcessSessionService(manager);

        for (var index = 0; index < 100; index++)
        {
            var view = await service.GetAsync();
            Assert.Equal("Processing", view.CurrentStep);
        }

        Assert.Equal(100, manager.Lists);
        Assert.Equal(0, manager.Starts);
        Assert.Equal(0, manager.Cancels);
        Assert.Equal(0, manager.Waits);
    }

    [Fact]
    public async Task Cancel_and_stop_delegate_to_the_manager()
    {
        var manager = new Manager { State = BackgroundTaskState.Running, WaitResult = false };
        manager.SetView(new ProcessSessionSnapshot(true, false, "Brand", new BookId("book-one"), "Processing", [new ProcessQueueEntry(new BookId("book-one"), BookProcessingStatus.Running, "Processing")]));
        var service = new ProcessSessionService(manager);

        var cancelling = await service.CancelAsync();
        var stopped = await service.StopAndWaitAsync(TimeSpan.FromMilliseconds(1));

        Assert.True(cancelling.IsCancelling);
        Assert.False(stopped);
        Assert.True(manager.Cancels >= 2);
        Assert.Equal(1, manager.Waits);
    }

    [Fact]
    public async Task StartAsync_rejects_invalid_book_request()
    {
        var service = new ProcessSessionService(new Manager());
        foreach (var ids in new[] { Array.Empty<string>(), new[] { "" }, new[] { "book", "book" } })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(ids, "Brand", BookProcessingMode.InteriorOnly).AsTask());
        }
    }

    private sealed class Manager : IBackgroundTaskManager
    {
        private readonly BackgroundTaskId id = new("processing-test");
        private ProcessSessionSnapshot? view;
        public object? Request { get; private set; }
        public BackgroundTaskState State { get; set; } = BackgroundTaskState.Queued;
        public int Starts { get; private set; }
        public int Lists { get; private set; }
        public int Cancels { get; private set; }
        public int Waits { get; private set; }
        public bool WaitResult { get; set; } = true;

        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default)
        {
            if (Request is null) { Starts++; Request = request; view = initialView as ProcessSessionSnapshot; }
            return ValueTask.FromResult(Snapshot());
        }
        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) => ValueTask.FromResult<BackgroundTaskSnapshot?>(Snapshot());
        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default) { Lists++; return ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>([Snapshot()]); }
        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) { Cancels++; if (State is BackgroundTaskState.Queued or BackgroundTaskState.Running) State = BackgroundTaskState.Cancelling; return ValueTask.FromResult<BackgroundTaskSnapshot?>(Snapshot()); }
        public ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) { Waits++; return ValueTask.FromResult(WaitResult); }
        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result) { result = default; return false; }
        public bool TryGetView<TView>(BackgroundTaskId taskId, out TView? result) where TView : class { result = view as TView; return result is not null; }
        public void SetView(ProcessSessionSnapshot value) => view = value;
        private BackgroundTaskSnapshot Snapshot() => new(id, BackgroundTaskKind.ProcessingSession, State, "processing", "book-one", null, null, null, null, DateTimeOffset.UtcNow, null, null, null);
    }
}
