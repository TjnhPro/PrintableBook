using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Desktop;

namespace PrintableBook.Desktop.Tests;

public sealed class ProcessWindowShutdownCoordinatorTests
{
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

    private sealed class StubPrompt(ActiveProcessCloseDecision activeDecision = ActiveProcessCloseDecision.StopAndExit, ProcessStopTimeoutDecision timeoutDecision = ProcessStopTimeoutDecision.ForceExit) : IProcessShutdownPrompt
    {
        public int ActivePromptCount { get; private set; }
        public int TimeoutPromptCount { get; private set; }
        public ActiveProcessCloseDecision ConfirmActiveProcessClose() { ActivePromptCount++; return activeDecision; }
        public ProcessStopTimeoutDecision ConfirmStopTimeout() { TimeoutPromptCount++; return timeoutDecision; }
    }

    private sealed class StubSession(bool active, IReadOnlyList<bool> stopResults) : IProcessSessionService
    {
        private readonly Queue<bool> results = new(stopResults);
        public int StopCalls { get; private set; }
        public List<TimeSpan> Timeouts { get; } = [];
        public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProcessSessionSnapshot(active, false, null, active ? new BookId("book") : null, null, []));
        public ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            Timeouts.Add(timeout);
            return ValueTask.FromResult(results.Dequeue());
        }
    }
}
