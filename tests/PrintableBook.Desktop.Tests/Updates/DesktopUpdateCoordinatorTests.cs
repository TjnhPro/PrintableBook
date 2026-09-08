using PrintableBook.Core.Application.Updates;
using PrintableBook.Desktop.Bridge;
using PrintableBook.Desktop.Updates;
using System.Text.Json;

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
    public async Task Failed_download_can_retry_with_a_fresh_progress_state()
    {
        var preparation = new StubPreparationService { Exception = new InvalidDataException("bad archive") };
        var coordinator = CreateCoordinator(new StubUpdateService { Result = AvailableResult() }, preparation);
        await coordinator.CheckAsync(UpdateCheckTrigger.Manual);

        var failed = await coordinator.DownloadAsync();
        preparation.Exception = null;
        preparation.ReportProgress = false;
        preparation.Completion = new TaskCompletionSource<PreparedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = coordinator.DownloadAsync().AsTask();
        await preparation.Started.Task;

        var retrying = coordinator.GetState();
        Assert.Equal(DesktopUpdatePhase.Error, failed.Phase);
        Assert.Equal("update_download_failed", failed.ErrorCode);
        Assert.True(failed.CanDownload);
        Assert.Equal(DesktopUpdatePhase.Downloading, retrying.Phase);
        Assert.Null(retrying.PreparationStage);
        Assert.Equal(0, retrying.BytesReceived);

        preparation.Completion.SetResult(new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\payload"));
        await retry;
    }

    [Fact]
    public async Task Duplicate_check_does_not_queue_a_second_service_call()
    {
        var updates = new StubUpdateService
        {
            Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            Completion = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var coordinator = CreateCoordinator(updates);
        var first = coordinator.CheckAsync(UpdateCheckTrigger.Manual).AsTask();
        await updates.Started.Task;

        var second = await coordinator.CheckAsync(UpdateCheckTrigger.Manual);

        Assert.Equal(DesktopUpdatePhase.Checking, second.Phase);
        Assert.Equal(1, updates.CallCount);
        updates.Completion.SetResult(AvailableResult());
        await first;
    }

    [Fact]
    public async Task Check_cancellation_restores_the_previous_available_state()
    {
        var updates = new StubUpdateService { Result = AvailableResult() };
        var coordinator = CreateCoordinator(updates);
        await coordinator.CheckAsync(UpdateCheckTrigger.Manual);
        updates.Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        updates.Completion = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var checking = coordinator.CheckAsync(UpdateCheckTrigger.Manual, cancellation.Token).AsTask();
        await updates.Started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checking);
        var restored = coordinator.GetState();
        Assert.Equal(DesktopUpdatePhase.Available, restored.Phase);
        Assert.Equal(new Version(0, 2, 0), restored.LatestRelease!.Version);
        Assert.True(restored.CanDownload);
    }

    [Theory]
    [InlineData(UpdatePreparationStage.DownloadingArchive, DesktopUpdatePhase.Downloading)]
    [InlineData(UpdatePreparationStage.DownloadingChecksum, DesktopUpdatePhase.Downloading)]
    [InlineData(UpdatePreparationStage.Verifying, DesktopUpdatePhase.Verifying)]
    [InlineData(UpdatePreparationStage.Extracting, DesktopUpdatePhase.Verifying)]
    [InlineData(UpdatePreparationStage.Validating, DesktopUpdatePhase.Verifying)]
    [InlineData(UpdatePreparationStage.Ready, DesktopUpdatePhase.Ready)]
    public async Task Preparation_stages_map_to_the_expected_desktop_phase(UpdatePreparationStage stage, DesktopUpdatePhase expectedPhase)
    {
        var preparation = new StubPreparationService
        {
            ReportProgress = false,
            Progress = [new UpdatePreparationProgress(stage, 512, 1024)],
            Completion = new TaskCompletionSource<PreparedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var coordinator = CreateCoordinator(new StubUpdateService { Result = AvailableResult() }, preparation);
        await coordinator.CheckAsync(UpdateCheckTrigger.Manual);
        var downloading = coordinator.DownloadAsync().AsTask();
        await preparation.Started.Task;

        Assert.Equal(expectedPhase, coordinator.GetState().Phase);
        preparation.Completion.SetResult(new PreparedUpdate(new Version(0, 2, 0), "D:\\updates\\payload"));
        await downloading;
    }

    [Fact]
    public async Task Download_cancellation_restores_the_previous_available_state()
    {
        var preparation = new StubPreparationService { Completion = new TaskCompletionSource<PreparedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously) };
        var coordinator = CreateCoordinator(new StubUpdateService { Result = AvailableResult() }, preparation);
        await coordinator.CheckAsync(UpdateCheckTrigger.Manual);
        using var cancellation = new CancellationTokenSource();
        var downloading = coordinator.DownloadAsync(cancellation.Token).AsTask();
        await preparation.Started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloading);
        var restored = coordinator.GetState();
        Assert.Equal(DesktopUpdatePhase.Available, restored.Phase);
        Assert.Equal(new Version(0, 2, 0), restored.LatestRelease!.Version);
        Assert.True(restored.CanDownload);
    }

    [Fact]
    public void Serialized_update_bridge_snapshot_excludes_private_package_details()
    {
        var snapshot = new DesktopUpdateSnapshot(DesktopUpdatePhase.Available, new Version(0, 1, 1), Update(), null, 0, null, null, null, null, true, true, false);
        var json = JsonSerializer.Serialize(UpdateBridgeSnapshot.From(snapshot), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"latestVersion\":\"0.2.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"releaseName\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PrintableBook.zip", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayloadDirectoryPath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("staging", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--wait-pid", json, StringComparison.Ordinal);
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

    [Fact]
    public async Task Bridge_requires_an_update_coordinator_and_defaults_a_missing_trigger_to_manual()
    {
        var unavailable = await new WebViewBridgeRouter().HandleAsync("""{"version":1,"id":"none","command":"updates.getState"}""");
        var coordinator = new RecordingCoordinator(CreateCoordinator().GetState());
        var response = await new WebViewBridgeRouter(updateCoordinator: coordinator)
            .HandleAsync("""{"version":1,"id":"check","command":"updates.check"}""");

        Assert.Equal("unsupported_command", unavailable.Error);
        Assert.True(response.Ok);
        Assert.Equal(UpdateCheckTrigger.Manual, coordinator.LastTrigger);
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
        public UpdateCheckResult Result { get; set; } = new(new Version(0, 1, 1), null, UpdateAvailability.UpToDate);
        public Exception? Exception { get; set; }
        public TaskCompletionSource<bool>? Started { get; set; }
        public TaskCompletionSource<UpdateCheckResult>? Completion { get; set; }

        public async ValueTask<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started?.TrySetResult(true);
            if (Exception is not null) throw Exception;
            if (Completion is not null) return await Completion.Task.WaitAsync(cancellationToken);
            return Result;
        }
    }

    private sealed class StubPreparationService : IUpdatePreparationService
    {
        public int CallCount { get; private set; }
        public PreparedUpdate Result { get; init; } = new(new Version(0, 2, 0), "D:\\updates\\payload");
        public TaskCompletionSource<PreparedUpdate>? Completion { get; set; }
        public Exception? Exception { get; set; }
        public bool ReportProgress { get; set; } = true;
        public IReadOnlyList<UpdatePreparationProgress> Progress { get; set; } = [];
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PreparedUpdate> PrepareAsync(UpdateInfo update, IProgress<UpdatePreparationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ReportProgress) progress?.Report(new UpdatePreparationProgress(UpdatePreparationStage.DownloadingArchive, 512, 1024));
            foreach (var item in Progress) progress?.Report(item);
            Started.TrySetResult(true);
            if (Exception is not null) throw Exception;
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

    private sealed class RecordingCoordinator(DesktopUpdateSnapshot snapshot) : IDesktopUpdateCoordinator
    {
        public UpdateCheckTrigger? LastTrigger { get; private set; }

        public DesktopUpdateSnapshot GetState() => snapshot;
        public ValueTask<DesktopUpdateSnapshot> CheckAsync(UpdateCheckTrigger trigger, CancellationToken cancellationToken = default)
        {
            LastTrigger = trigger;
            return ValueTask.FromResult(snapshot);
        }
        public ValueTask<DesktopUpdateSnapshot> DownloadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
        public ValueTask<DesktopUpdateSnapshot> InstallAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }
}
