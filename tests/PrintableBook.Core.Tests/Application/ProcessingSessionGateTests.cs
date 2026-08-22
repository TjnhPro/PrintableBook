using PrintableBook.Core.Application.Execution;

namespace PrintableBook.Core.Tests.Application;

public sealed class ProcessingSessionGateTests
{
    [Fact]
    public async Task TryAcquireAsync_rejects_a_second_session_until_the_first_is_released()
    {
        var gate = new ProcessingSessionGate();
        await using var firstSession = await gate.TryAcquireAsync();

        var rejectedSession = await gate.TryAcquireAsync();

        Assert.NotNull(firstSession);
        Assert.Null(rejectedSession);
        Assert.True(gate.IsRunning);
    }

    [Fact]
    public async Task Disposing_a_session_returns_the_gate_to_idle_after_a_failure_path()
    {
        var gate = new ProcessingSessionGate();
        var session = await gate.TryAcquireAsync();

        await session!.DisposeAsync();

        Assert.False(gate.IsRunning);
        Assert.NotNull(await gate.TryAcquireAsync());
    }

    [Fact]
    public async Task TryAcquireAsync_honours_a_pre_cancelled_request()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProcessingSessionGate().TryAcquireAsync(cancellation.Token).AsTask());
    }
}
