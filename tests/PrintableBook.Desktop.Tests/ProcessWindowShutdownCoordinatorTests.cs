using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Desktop;

namespace PrintableBook.Desktop.Tests;

public sealed class ProcessWindowShutdownCoordinatorTests
{
    [Fact]
    public void ShouldHandleInteractiveClose_keeps_normal_user_close_on_the_coordinator_path()
    {
        Assert.True(MainWindow.ShouldHandleInteractiveClose(allowClose: false, systemShutdown: false, updateShutdownRequested: false));
    }

    [Fact]
    public void ShouldHandleInteractiveClose_bypasses_the_coordinator_during_system_shutdown()
    {
        Assert.False(MainWindow.ShouldHandleInteractiveClose(allowClose: false, systemShutdown: true, updateShutdownRequested: false));
    }

    [Fact]
    public void ShouldHandleInteractiveClose_bypasses_the_coordinator_after_an_update_shutdown_is_requested()
    {
        Assert.False(MainWindow.ShouldHandleInteractiveClose(allowClose: false, systemShutdown: false, updateShutdownRequested: true));
    }

    [Fact]
    public async Task RequestCloseAsync_closes_an_inactive_session_without_prompting()
    {
        var prompt = new StubPrompt();
        var session = new StubSession(false, []);

        var outcome = await new ProcessWindowShutdownCoordinator(session, prompt).RequestCloseAsync();

        Assert.Equal(ProcessWindowCloseOutcome.Close, outcome);
        Assert.Equal(0, prompt.ActivePromptCount);
        Assert.Equal(0, session.StopCalls);
    }

    [Fact]
    public async Task RequestCloseAsync_keeps_the_app_open_when_the_user_continues()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.ContinueUsingApp);
        var session = new StubSession(true, []);

        var outcome = await new ProcessWindowShutdownCoordinator(session, prompt).RequestCloseAsync();

        Assert.Equal(ProcessWindowCloseOutcome.KeepOpen, outcome);
        Assert.Equal(0, session.StopCalls);
    }

    [Fact]
    public async Task RequestCloseAsync_repeats_bounded_waits_until_the_process_stops()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.StopAndExit, ProcessStopTimeoutDecision.KeepWaiting);
        var session = new StubSession(true, [false, true]);

        var outcome = await new ProcessWindowShutdownCoordinator(session, prompt).RequestCloseAsync();

        Assert.Equal(ProcessWindowCloseOutcome.Close, outcome);
        Assert.Equal(2, session.StopCalls);
        Assert.Equal(1, prompt.TimeoutPromptCount);
        Assert.All(session.Timeouts, timeout => Assert.Equal(TimeSpan.FromSeconds(5), timeout));
    }

    [Fact]
    public async Task RequestCloseAsync_can_force_exit_after_a_timeout()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.StopAndExit, ProcessStopTimeoutDecision.ForceExit);
        var session = new StubSession(true, [false]);

        var outcome = await new ProcessWindowShutdownCoordinator(session, prompt).RequestCloseAsync();

        Assert.Equal(ProcessWindowCloseOutcome.ForceExit, outcome);
    }

    [Fact]
    public async Task RequestCloseAsync_does_not_prompt_for_a_timeout_when_system_shutdown_cancels_the_interactive_close_flow()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.StopAndExit, ProcessStopTimeoutDecision.ForceExit);
        var session = new BlockingStopSession();
        var coordinator = new ProcessWindowShutdownCoordinator(session, prompt);
        using var shutdown = new CancellationTokenSource();

        var closing = coordinator.RequestCloseAsync(shutdown.Token).AsTask();
        await session.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        shutdown.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
        Assert.Equal(1, prompt.ActivePromptCount);
        Assert.Equal(0, prompt.TimeoutPromptCount);
    }

    [Fact]
    public async Task RequestUpdateRestartAsync_uses_the_existing_shutdown_algorithm_with_update_intent()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.StopAndExit, ProcessStopTimeoutDecision.KeepWaiting);
        var session = new StubSession(true, [false, true]);

        var outcome = await new ProcessWindowShutdownCoordinator(session, prompt).RequestUpdateRestartAsync();

        Assert.Equal(ProcessWindowCloseOutcome.Close, outcome);
        Assert.All(prompt.ActiveIntents, intent => Assert.Equal(ProcessShutdownIntent.RestartForUpdate, intent));
        Assert.All(prompt.TimeoutIntents, intent => Assert.Equal(ProcessShutdownIntent.RestartForUpdate, intent));
    }

    [Fact]
    public async Task RequestCloseAsync_prompts_cancels_and_drains_an_active_s3_publication()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.StopAndExit);
        var session = new StubSession(false, []);
        var storage = new StubBackgroundTaskManager([true]);

        var outcome = await new ProcessWindowShutdownCoordinator(session, prompt, backgroundTaskManager: storage).RequestCloseAsync();

        Assert.Equal(ProcessWindowCloseOutcome.Close, outcome);
        Assert.Equal(1, prompt.ActivePromptCount);
        Assert.Equal(1, storage.CancelCalls);
        Assert.Equal(1, storage.WaitCalls);
        Assert.Equal(ProcessWindowShutdownCoordinator.StopTimeout, storage.Timeouts.Single());
    }

    [Fact]
    public async Task RequestCloseAsync_keeps_active_s3_running_when_the_user_continues_using_the_app()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.ContinueUsingApp);
        var storage = new StubBackgroundTaskManager([true]);

        var outcome = await new ProcessWindowShutdownCoordinator(new StubSession(false, []), prompt, backgroundTaskManager: storage).RequestCloseAsync();

        Assert.Equal(ProcessWindowCloseOutcome.KeepOpen, outcome);
        Assert.Equal(0, storage.CancelCalls);
        Assert.Equal(0, storage.WaitCalls);
    }

    [Fact]
    public async Task RequestCloseAsync_can_force_exit_when_s3_does_not_drain_within_the_timeout()
    {
        var prompt = new StubPrompt(ActiveProcessCloseDecision.StopAndExit, ProcessStopTimeoutDecision.ForceExit);
        var storage = new StubBackgroundTaskManager([false]);

        var outcome = await new ProcessWindowShutdownCoordinator(new StubSession(false, []), prompt, backgroundTaskManager: storage).RequestCloseAsync();

        Assert.Equal(ProcessWindowCloseOutcome.ForceExit, outcome);
        Assert.Equal(1, prompt.TimeoutPromptCount);
    }

    private sealed class StubPrompt(ActiveProcessCloseDecision activeDecision = ActiveProcessCloseDecision.StopAndExit, ProcessStopTimeoutDecision timeoutDecision = ProcessStopTimeoutDecision.ForceExit) : IProcessShutdownPrompt
    {
        public int ActivePromptCount { get; private set; }
        public int TimeoutPromptCount { get; private set; }
        public List<ProcessShutdownIntent> ActiveIntents { get; } = [];
        public List<ProcessShutdownIntent> TimeoutIntents { get; } = [];
        public ActiveProcessCloseDecision ConfirmActiveProcessClose(ProcessShutdownIntent intent) { ActivePromptCount++; ActiveIntents.Add(intent); return activeDecision; }
        public ProcessStopTimeoutDecision ConfirmStopTimeout(ProcessShutdownIntent intent) { TimeoutPromptCount++; TimeoutIntents.Add(intent); return timeoutDecision; }
    }

    private sealed class StubSession(bool active, IReadOnlyList<bool> stopResults) : IProcessSessionService
    {
        private readonly Queue<bool> results = new(stopResults);
        public int StopCalls { get; private set; }
        public List<TimeSpan> Timeouts { get; } = [];
        public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProcessSessionSnapshot(active, false, null, active ? new BookId("book") : null, null, []));
        public ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, BookProcessingMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            Timeouts.Add(timeout);
            return ValueTask.FromResult(results.Dequeue());
        }
    }

    private sealed class BlockingStopSession : IProcessSessionService
    {
        public TaskCompletionSource StopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProcessSessionSnapshot(true, false, null, new BookId("book"), null, []));

        public ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, BookProcessingMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            StopStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }
    }

    private sealed class StubBackgroundTaskManager(IReadOnlyList<bool> waitResults) : IBackgroundTaskManager
    {
        private readonly BackgroundTaskId taskId = new("task-s3-shutdown");
        private readonly Queue<bool> waits = new(waitResults);
        private bool active = true;
        public int CancelCalls { get; private set; }
        public int WaitCalls { get; private set; }
        public List<TimeSpan> Timeouts { get; } = [];

        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(BackgroundTaskKind kind, string key, string? subject, TRequest request, object? initialView = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId requestedTaskId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BackgroundTaskSnapshot?>(requestedTaskId == taskId ? Snapshot() : null);

        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(BackgroundTaskKind? kind = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>(active && (kind is null or BackgroundTaskKind.S3Storage) ? [Snapshot()] : []);

        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId requestedTaskId, CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            return ValueTask.FromResult<BackgroundTaskSnapshot?>(Snapshot(BackgroundTaskState.Cancelling));
        }

        public ValueTask<bool> WaitAsync(BackgroundTaskId requestedTaskId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            WaitCalls++;
            Timeouts.Add(timeout);
            var completed = waits.Dequeue();
            if (completed) active = false;
            return ValueTask.FromResult(completed);
        }

        public bool TryGetResult<TResult>(BackgroundTaskId requestedTaskId, out TResult? result)
        {
            result = default;
            return false;
        }

        public bool TryGetView<TView>(BackgroundTaskId requestedTaskId, out TView? view) where TView : class
        {
            view = default;
            return false;
        }

        private BackgroundTaskSnapshot Snapshot(BackgroundTaskState state = BackgroundTaskState.Running) =>
            new(taskId, BackgroundTaskKind.S3Storage, state, "s3:book:Upload", "Book One", "publishing", 2, 7, null, DateTimeOffset.UtcNow, null, null, null);
    }
}
