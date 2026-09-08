using PrintableBook.Core.Application.Updates;
using PrintableBook.Desktop.Updates;

namespace PrintableBook.Desktop.Tests.Updates;

public sealed class DesktopUpdateCoordinatorTests
{
    [Fact]
    public void InitialStateReportsCurrentVersionAndOnlyCheckCapability()
    {
        var coordinator = new DesktopUpdateCoordinator(
            new StubUpdateService(),
            new StubPreparationService(),
            new StubVersionProvider(new Version(0, 1, 1)),
            new StubInstallHandoff());

        var state = coordinator.GetState();

        Assert.Equal(DesktopUpdatePhase.Idle, state.Phase);
        Assert.Equal(new Version(0, 1, 1), state.CurrentVersion);
        Assert.True(state.CanCheck);
        Assert.False(state.CanDownload);
        Assert.False(state.CanInstall);
        Assert.Null(state.LatestRelease);
        Assert.Null(state.ErrorCode);
    }

    private sealed class StubVersionProvider(Version version) : IApplicationVersionProvider
    {
        public Version CurrentVersion { get; } = version;
    }

    private sealed class StubUpdateService : IUpdateService
    {
        public ValueTask<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubPreparationService : IUpdatePreparationService
    {
        public ValueTask<PreparedUpdate> PrepareAsync(UpdateInfo update, IProgress<UpdatePreparationProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubInstallHandoff : IUpdateInstallHandoff
    {
        public ValueTask<UpdateInstallHandoffOutcome> BeginAsync(PreparedUpdate preparedUpdate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
