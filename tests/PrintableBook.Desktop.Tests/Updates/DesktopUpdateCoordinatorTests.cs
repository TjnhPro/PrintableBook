using PrintableBook.Core.Application.Updates;
using PrintableBook.Desktop.Bridge;
using PrintableBook.Desktop.Updates;

namespace PrintableBook.Desktop.Tests.Updates;

public sealed class DesktopUpdateCoordinatorTests
{
    [Fact]
    public void InitialStateReportsCurrentVersionAndOnlyCheckCapability()
    {
        var coordinator = CreateCoordinator();

        var state = coordinator.GetState();

        Assert.Equal(DesktopUpdatePhase.Idle, state.Phase);
        Assert.Equal(new Version(0, 1, 1), state.CurrentVersion);
        Assert.True(state.CanCheck);
        Assert.False(state.CanDownload);
        Assert.False(state.CanInstall);
        Assert.Null(state.LatestRelease);
        Assert.Null(state.ErrorCode);
    }

    [Fact]
    public async Task Check_download_and_install_follow_the_update_lifecycle()
    {
        var updates = new StubUpdateService { Result = AvailableResult() };
        var preparation = new StubPreparationService { Result = new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\payload") };
        var handoff = new StubInstallHandoff();
        var coordinator = CreateCoordinator(updates, preparation, handoff);

        var available = await coordinator.CheckAsync(UpdateCheckTrigger.Manual);
        var ready = await coordinator.DownloadAsync();
        var installing = await coordinator.InstallAsync();

        Assert.Equal(DesktopUpdatePhase.Available, available.Phase);
        Assert.Equal(DesktopUpdatePhase.Ready, ready.Phase);
        Assert.Equal(DesktopUpdatePhase.Installing, installing.Phase);
        Assert.Equal(1, updates.CallCount);
        Assert.Equal(1, preparation.CallCount);
        Assert.Equal(1, handoff.CallCount);
        Assert.Equal(UpdatePreparationStage.Ready, ready.PreparationStage);
    }

    [Fact]
    public async Task Automatic_check_failure_restores_the_previous_stable_state_but_manual_failure_is_retryable()
    {
        var updates = new StubUpdateService { Exception = new HttpRequestException("network") };
        var coordinator = CreateCoordinator(updates);

        var automatic = await coordinator.CheckAsync(UpdateCheckTrigger.Automatic);
        var manual = await coordinator.CheckAsync(UpdateCheckTrigger.Manual);

        Assert.Equal(DesktopUpdatePhase.Idle, automatic.Phase);
        Assert.Equal(DesktopUpdatePhase.Error, manual.Phase);
        Assert.Equal("update_check_failed", manual.ErrorCode);
        Assert.Equal("Could not check for updates. Check your internet connection and try again.", manual.ErrorMessage);
        Assert.True(manual.CanCheck);
    }

    [Fact]
    public async Task Get_state_and_duplicate_download_return_promptly_while_preparation_is_running()
    {
        var updates = new StubUpdateService { Result = AvailableResult() };
        var preparation = new StubPreparationService { Completion = new TaskCompletionSource<PreparedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously) };
        var coordinator = CreateCoordinator(updates, preparation);
        await coordinator.CheckAsync(UpdateCheckTrigger.Manual);
        var router = new WebViewBridgeRouter(updateCoordinator: coordinator);

        var download = router.HandleAsync("""{"version":1,"id":"download","command":"updates.download"}""").AsTask();
        await preparation.Started.Task;

        var getState = await router.HandleAsync("""{"version":1,"id":"state","command":"updates.getState"}""");
        var duplicate = await router.HandleAsync("""{"version":1,"id":"duplicate","command":"updates.download"}""");

        Assert.Equal("updates.state", getState.Command);
        Assert.Equal("Downloading", Assert.IsType<UpdateBridgeSnapshot>(getState.Payload).Phase);
        Assert.Equal("Downloading", Assert.IsType<UpdateBridgeSnapshot>(duplicate.Payload).Phase);
        Assert.Equal(1, preparation.CallCount);

        preparation.Completion.SetResult(new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\payload"));
        var completed = await download;
        Assert.Equal("Ready", Assert.IsType<UpdateBridgeSnapshot>(completed.Payload).Phase);
    }

    [Fact]
    public async Task Install_launch_failure_preserves_the_prepared_update_for_retry()
    {
        var handoff = new StubInstallHandoff { Exception = new InvalidOperationException("do not expose this") };
        var coordinator = await CreateReadyCoordinatorAsync(handoff);

        var result = await coordinator.InstallAsync();

        Assert.Equal(DesktopUpdatePhase.Error, result.Phase);
        Assert.Equal("update_install_failed", result.ErrorCode);
        Assert.True(result.CanInstall);
        Assert.DoesNotContain("do not expose this", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bridge_install_cancel_and_failure_return_sanitized_retryable_states()
    {
        var cancelledCoordinator = await CreateReadyCoordinatorAsync(new StubInstallHandoff { Outcome = UpdateInstallHandoffOutcome.Cancelled });
        var cancelled = await new WebViewBridgeRouter(updateCoordinator: cancelledCoordinator)
            .HandleAsync("""{"version":1,"id":"cancel","command":"updates.install"}""");

        var cancelledState = Assert.IsType<UpdateBridgeSnapshot>(cancelled.Payload);
        Assert.True(cancelled.Ok);
        Assert.Equal("updates.state", cancelled.Command);
        Assert.Equal("Ready", cancelledState.Phase);
        Assert.True(cancelledState.CanInstall);

        var failureCoordinator = await CreateReadyCoordinatorAsync(new StubInstallHandoff { Exception = new InvalidOperationException("internal launch detail") });
        var failed = await new WebViewBridgeRouter(updateCoordinator: failureCoordinator)
            .HandleAsync("""{"version":1,"id":"failure","command":"updates.install"}""");

        var failedState = Assert.IsType<UpdateBridgeSnapshot>(failed.Payload);
        Assert.True(failed.Ok);
        Assert.Equal("Error", failedState.Phase);
        Assert.Equal("update_install_failed", failedState.ErrorCode);
        Assert.True(failedState.CanInstall);
        Assert.DoesNotContain("internal launch detail", failedState.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("0")]
    public async Task Bridge_rejects_invalid_update_check_triggers(string trigger)
    {
        var response = await new WebViewBridgeRouter(updateCoordinator: CreateCoordinator())
            .HandleAsync($"{{\"version\":1,\"id\":\"bad-trigger\",\"command\":\"updates.check\",\"payload\":{{\"trigger\":\"{trigger}\"}}}}");

        Assert.False(response.Ok);
        Assert.Equal("invalid_update_check_trigger", response.Error);
    }

    private static async Task<DesktopUpdateCoordinator> CreateReadyCoordinatorAsync(StubInstallHandoff handoff)
    {
        var coordinator = CreateCoordinator(
            new StubUpdateService { Result = AvailableResult() },
            new StubPreparationService { Result = new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\payload") },
            handoff);
        await coordinator.CheckAsync(UpdateCheckTrigger.Manual);
        await coordinator.DownloadAsync();
        return coordinator;
    }

    private static DesktopUpdateCoordinator CreateCoordinator(StubUpdateService? updates = null, StubPreparationService? preparation = null, StubInstallHandoff? handoff = null) => new(
        updates ?? new StubUpdateService(),
        preparation ?? new StubPreparationService(),
        new StubVersionProvider(new Version(0, 1, 1)),
        handoff ?? new StubInstallHandoff());

    private static UpdateCheckResult AvailableResult() => new(new Version(0, 1, 1), Update(), UpdateAvailability.Available);

    private static UpdateInfo Update() => new(
        new Version(0, 2, 0), "v0.2.0", "Printable Book 0.2.0", "Release notes", DateTimeOffset.UtcNow,
        new Uri("https://example.test/releases/v0.2.0"),
        new UpdatePackageInfo(
            new UpdateAssetInfo("PrintableBook.zip", new Uri("https://example.test/PrintableBook.zip"), 1024),
            new UpdateAssetInfo("PrintableBook.zip.sha256", new Uri("https://example.test/PrintableBook.zip.sha256"), 64)));

    private sealed class StubVersionProvider(Version version) : IApplicationVersionProvider
    {
        public Version CurrentVersion { get; } = version;
    }

    private sealed class StubUpdateService : IUpdateService
    {
        public int CallCount { get; private set; }
        public UpdateCheckResult Result { get; init; } = new(new Version(0, 1, 1), null, UpdateAvailability.UpToDate);
        public Exception? Exception { get; init; }

        public ValueTask<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Exception is not null) throw Exception;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class StubPreparationService : IUpdatePreparationService
    {
        public int CallCount { get; private set; }
        public PreparedUpdate Result { get; init; } = new(new Version(0, 2, 0), "D:\\updates\\payload");
        public TaskCompletionSource<PreparedUpdate>? Completion { get; init; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PreparedUpdate> PrepareAsync(UpdateInfo update, IProgress<UpdatePreparationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            progress?.Report(new UpdatePreparationProgress(UpdatePreparationStage.DownloadingArchive, 512, 1024));
            Started.TrySetResult(true);
            return Completion is null ? Result : await Completion.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class StubInstallHandoff : IUpdateInstallHandoff
    {
        public int CallCount { get; private set; }
        public UpdateInstallHandoffOutcome Outcome { get; init; } = UpdateInstallHandoffOutcome.ShutdownScheduled;
        public Exception? Exception { get; init; }

        public ValueTask<UpdateInstallHandoffOutcome> BeginAsync(PreparedUpdate preparedUpdate, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Exception is not null) throw Exception;
            return ValueTask.FromResult(Outcome);
        }
    }
}
