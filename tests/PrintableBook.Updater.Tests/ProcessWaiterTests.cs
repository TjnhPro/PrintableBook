using System.Diagnostics;
using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class ProcessWaiterTests
{
    [Fact]
    public async Task WaitForExitAsyncReturnsTrueForMissingProcess()
    {
        var missingPid = Enumerable.Range(0, 1000).Select(offset => int.MaxValue - offset)
            .First(pid => Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid)) is not null);
        Assert.True(await new ProcessWaiter().WaitForExitAsync(missingPid, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task WaitForExitAsyncTimesOutForCurrentProcess()
    {
        Assert.False(await new ProcessWaiter().WaitForExitAsync(Environment.ProcessId, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task WaitForExitAsyncHonorsExternalCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new ProcessWaiter().WaitForExitAsync(Environment.ProcessId, TimeSpan.FromSeconds(1), source.Token));
    }
}
