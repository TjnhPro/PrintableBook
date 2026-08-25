using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Desktop.Loading;

namespace PrintableBook.Desktop.Tests;

public sealed class ApplicationLoadCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_coalesces_concurrent_callers_into_one_snapshot_refresh()
    {
        var snapshots = new BlockingSnapshotService();
        var recovery = new RecordingRecoveryService();
        var coordinator = new ApplicationLoadCoordinator(snapshots, recovery);

        var first = coordinator.RefreshAsync(ApplicationLoadKind.Initial).AsTask();
        var second = coordinator.RefreshAsync(ApplicationLoadKind.Refresh).AsTask();
        await snapshots.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, snapshots.RefreshCount);
        snapshots.Complete(CreateSnapshot());

        Assert.Same(await first, await second);
        Assert.Equal(1, recovery.Calls);
    }

    [Fact]
    public async Task RefreshAsync_cancels_only_the_callers_wait_and_keeps_the_shared_refresh_running()
    {
        var snapshots = new BlockingSnapshotService();
        var coordinator = new ApplicationLoadCoordinator(snapshots, new RecordingRecoveryService());
        using var cancelledWait = new CancellationTokenSource();

        var first = coordinator.RefreshAsync(ApplicationLoadKind.Initial).AsTask();
        await snapshots.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = coordinator.RefreshAsync(ApplicationLoadKind.Refresh, cancelledWait.Token).AsTask();
        cancelledWait.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(1, snapshots.RefreshCount);
        snapshots.Complete(CreateSnapshot());

        Assert.NotNull(await first);
    }

    [Fact]
    public async Task RefreshAsync_starts_a_new_refresh_after_a_previous_failure()
    {
        var snapshots = new SequencedSnapshotService(new InvalidDataException("scan failed"), CreateSnapshot());
        var coordinator = new ApplicationLoadCoordinator(snapshots, new RecordingRecoveryService());

        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.RefreshAsync(ApplicationLoadKind.Initial).AsTask());
        var snapshot = await coordinator.RefreshAsync(ApplicationLoadKind.Refresh);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshots.RefreshCount);
    }

    [Fact]
    public async Task RefreshAsync_recovers_before_the_first_snapshot_and_only_once_after_success()
    {
        var events = new List<string>();
        var snapshots = new RecordingSnapshotService(events);
        var recovery = new RecordingRecoveryService(events);
        var coordinator = new ApplicationLoadCoordinator(snapshots, recovery);

        await coordinator.RefreshAsync(ApplicationLoadKind.Initial);
        await coordinator.RefreshAsync(ApplicationLoadKind.Refresh);

        Assert.Equal(["recover", "snapshot", "snapshot"], events);
    }

    [Fact]
    public async Task RefreshAsync_retries_recovery_when_the_first_recovery_fails()
    {
        var recovery = new SequencedRecoveryService(new InvalidDataException("recovery failed"), null);
        var snapshots = new RecordingSnapshotService([]);
        var coordinator = new ApplicationLoadCoordinator(snapshots, recovery);

        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.RefreshAsync(ApplicationLoadKind.Initial).AsTask());
        await coordinator.RefreshAsync(ApplicationLoadKind.Refresh);

        Assert.Equal(2, recovery.Calls);
        Assert.Equal(1, snapshots.RefreshCount);
    }

    private static ApplicationSnapshot CreateSnapshot() => new(
        new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], []),
        GlobalSettings.Default,
        [],
        DateTimeOffset.UtcNow);

    private sealed class BlockingSnapshotService : IApplicationSnapshotService
    {
        private readonly TaskCompletionSource<ApplicationSnapshot> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RefreshCount { get; private set; }

        public async ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            Started.TrySetResult();
            return await completion.Task;
        }

        public void Complete(ApplicationSnapshot snapshot) => completion.TrySetResult(snapshot);
    }

    private sealed class SequencedSnapshotService(params object[] results) : IApplicationSnapshotService
    {
        private readonly Queue<object> results = new(results);
        public int RefreshCount { get; private set; }

        public ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return results.Dequeue() switch
            {
                Exception exception => ValueTask.FromException<ApplicationSnapshot>(exception),
                ApplicationSnapshot snapshot => ValueTask.FromResult(snapshot),
                _ => throw new InvalidOperationException()
            };
        }
    }

    private sealed class RecordingSnapshotService(List<string> events) : IApplicationSnapshotService
    {
        public int RefreshCount { get; private set; }

        public ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            events.Add("snapshot");
            return ValueTask.FromResult(CreateSnapshot());
        }
    }

    private sealed class RecordingRecoveryService(List<string>? events = null) : IInterruptedProcessingRecoveryService
    {
        public int Calls { get; private set; }

        public ValueTask RecoverAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            events?.Add("recover");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequencedRecoveryService(params Exception?[] results) : IInterruptedProcessingRecoveryService
    {
        private readonly Queue<Exception?> results = new(results);
        public int Calls { get; private set; }

        public ValueTask RecoverAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = results.Dequeue();
            return result is null ? ValueTask.CompletedTask : ValueTask.FromException(result);
        }
    }
}
