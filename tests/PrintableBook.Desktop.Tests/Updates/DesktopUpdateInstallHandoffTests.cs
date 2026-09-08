using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.Updates;
using PrintableBook.Desktop.Updates;

namespace PrintableBook.Desktop.Tests.Updates;

public sealed class DesktopUpdateInstallHandoffTests
{
    [Fact]
    public async Task Graceful_handoff_launches_the_staged_updater_before_requesting_shutdown()
    {
        var events = new List<string>();
        var launcher = new RecordingLauncher(events);
        var lifetime = new RecordingLifetime(events);
        var handoff = CreateHandoff(new StubProcessSessionService(), new StubPrompt(), launcher, lifetime);

        var outcome = await handoff.BeginAsync(new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\staging\\0.2.0\\payload"));

        Assert.Equal(UpdateInstallHandoffOutcome.ShutdownScheduled, outcome);
        Assert.Equal(["launch", "graceful"], events);
        Assert.Equal(1234, launcher.Request!.WaitPid);
        Assert.Equal("D:\\PrintableBook", launcher.Request.AppRoot);
        Assert.Equal("D:\\updates\\staging\\0.2.0\\payload", launcher.Request.PayloadDirectory);
        Assert.Equal("D:\\updates", launcher.Request.UpdatesRoot);
        Assert.Equal(new Version(0, 1, 1), launcher.Request.CurrentVersion);
    }

    [Fact]
    public async Task Continue_using_the_application_skips_launcher_and_lifetime_requests()
    {
        var events = new List<string>();
        var launcher = new RecordingLauncher(events);
        var lifetime = new RecordingLifetime(events);
        var handoff = CreateHandoff(
            new StubProcessSessionService { Snapshot = ActiveSnapshot },
            new StubPrompt { ActiveDecision = ActiveProcessCloseDecision.ContinueUsingApp },
            launcher,
            lifetime);

        var outcome = await handoff.BeginAsync(new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\staging\\0.2.0\\payload"));

        Assert.Equal(UpdateInstallHandoffOutcome.Cancelled, outcome);
        Assert.Empty(events);
        Assert.Null(launcher.Request);
    }

    [Fact]
    public async Task Forced_handoff_launches_before_requesting_force_exit()
    {
        var events = new List<string>();
        var launcher = new RecordingLauncher(events);
        var lifetime = new RecordingLifetime(events);
        var handoff = CreateHandoff(
            new StubProcessSessionService { Snapshot = ActiveSnapshot, StopResult = false },
            new StubPrompt { ActiveDecision = ActiveProcessCloseDecision.StopAndExit, TimeoutDecision = ProcessStopTimeoutDecision.ForceExit },
            launcher,
            lifetime);

        var outcome = await handoff.BeginAsync(new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\staging\\0.2.0\\payload"));

        Assert.Equal(UpdateInstallHandoffOutcome.ShutdownScheduled, outcome);
        Assert.Equal(["launch", "force"], events);
    }

    [Fact]
    public async Task Launcher_failure_keeps_the_desktop_open()
    {
        var events = new List<string>();
        var launcher = new RecordingLauncher(events) { Exception = new InvalidOperationException("launch failed") };
        var lifetime = new RecordingLifetime(events);
        var handoff = CreateHandoff(new StubProcessSessionService(), new StubPrompt(), launcher, lifetime);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.BeginAsync(new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\staging\\0.2.0\\payload")).AsTask());

        Assert.Equal(["launch"], events);
    }

    private static DesktopUpdateInstallHandoff CreateHandoff(
        StubProcessSessionService process,
        StubPrompt prompt,
        RecordingLauncher launcher,
        RecordingLifetime lifetime) => new(
        new ProcessWindowShutdownCoordinator(process, prompt),
        launcher,
        lifetime,
        new StubRuntimeInfo(),
        new StubVersionProvider(),
        new UpdateStorageLayout(new StubStorageRootProvider()));

    private static readonly ProcessSessionSnapshot ActiveSnapshot = new(true, false, "Brand", null, "Processing", []);

    private sealed class StubProcessSessionService : IProcessSessionService
    {
        public ProcessSessionSnapshot Snapshot { get; init; } = new(false, false, null, null, null, []);
        public bool StopResult { get; init; } = true;

        public ValueTask<ProcessSessionSnapshot> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
        public ValueTask<ProcessSessionSnapshot> StartAsync(IReadOnlyList<string> bookIds, string? brandName, BookProcessingMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProcessSessionSnapshot> CancelAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> StopAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => ValueTask.FromResult(StopResult);
    }

    private sealed class StubPrompt : IProcessShutdownPrompt
    {
        public ActiveProcessCloseDecision ActiveDecision { get; init; } = ActiveProcessCloseDecision.StopAndExit;
        public ProcessStopTimeoutDecision TimeoutDecision { get; init; } = ProcessStopTimeoutDecision.KeepWaiting;

        public ActiveProcessCloseDecision ConfirmActiveProcessClose(ProcessShutdownIntent intent) => ActiveDecision;
        public ProcessStopTimeoutDecision ConfirmStopTimeout(ProcessShutdownIntent intent) => TimeoutDecision;
    }

    private sealed class RecordingLauncher(List<string> events) : IUpdaterProcessLauncher
    {
        public UpdaterLaunchRequest? Request { get; private set; }
        public Exception? Exception { get; init; }

        public void Launch(UpdaterLaunchRequest request)
        {
            Request = request;
            events.Add("launch");
            if (Exception is not null) throw Exception;
        }
    }

    private sealed class RecordingLifetime(List<string> events) : IUpdateApplicationLifetime
    {
        public void RequestGracefulShutdown() => events.Add("graceful");
        public void RequestForceExit() => events.Add("force");
    }

    private sealed class StubRuntimeInfo : IUpdateRuntimeInfo
    {
        public int ProcessId => 1234;
        public string AppRoot => "D:\\PrintableBook";
    }

    private sealed class StubVersionProvider : IApplicationVersionProvider
    {
        public Version CurrentVersion => new(0, 1, 1);
    }

    private sealed class StubStorageRootProvider : IUpdateStorageRootProvider
    {
        public string RootPath => "D:\\updates";
    }
}
