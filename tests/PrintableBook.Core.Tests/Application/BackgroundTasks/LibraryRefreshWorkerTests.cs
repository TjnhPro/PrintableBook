using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Core.Tests.Application.BackgroundTasks;

public sealed class LibraryRefreshWorkerTests
{
    [Fact]
    public async Task First_successful_execution_recovers_then_refreshes_and_later_execution_skips_recovery()
    {
        var events = new List<string>();
        var worker = new LibraryRefreshWorker(new Recovery(events), new Snapshots(events));
        IBackgroundTaskWorker adapter = worker;

        await adapter.ExecuteAsync(new LibraryRefreshRequest(), new Context(), CancellationToken.None);
        await adapter.ExecuteAsync(new LibraryRefreshRequest(), new Context(), CancellationToken.None);

        Assert.Equal(["recover", "snapshot", "snapshot"], events);
    }

    [Fact]
    public async Task Failed_recovery_is_retried_by_the_next_execution()
    {
        var recovery = new Recovery([], failsFirst: true);
        IBackgroundTaskWorker worker = new LibraryRefreshWorker(recovery, new Snapshots([]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteAsync(new LibraryRefreshRequest(), new Context(), CancellationToken.None).AsTask());
        await worker.ExecuteAsync(new LibraryRefreshRequest(), new Context(), CancellationToken.None);

        Assert.Equal(2, recovery.Calls);
    }

    private sealed class Context : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("task-test");
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class { }
    }

    private sealed class Recovery(List<string> events, bool failsFirst = false) : IInterruptedProcessingRecoveryService
    {
        public int Calls { get; private set; }
        public ValueTask RecoverAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (failsFirst && Calls == 1) return ValueTask.FromException(new InvalidOperationException("recovery failed"));
            events.Add("recover");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Snapshots(List<string> events) : IApplicationSnapshotService
    {
        public ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            events.Add("snapshot");
            return ValueTask.FromResult(new ApplicationSnapshot(
                new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], []),
                GlobalSettings.Default, [], DateTimeOffset.UtcNow));
        }
    }
}
