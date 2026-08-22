using PrintableBook.Core.Application.Execution;

namespace PrintableBook.Core.Tests.Application;

public sealed class BookPageConcurrencyControllerTests
{
    [Fact]
    public void Create_clamps_configured_concurrency_to_the_supported_range()
    {
        Assert.Equal(1, BookPageConcurrencyController.Create(-5).MaximumConcurrency);
        Assert.Equal(12, BookPageConcurrencyController.Create(99).MaximumConcurrency);
    }

    [Fact]
    public async Task RunAsync_never_exceeds_the_configured_page_limit()
    {
        var controller = BookPageConcurrencyController.Create(2);
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var highestObserved = 0;

        async Task Work(CancellationToken cancellationToken)
        {
            var nowActive = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref highestObserved, nowActive);
            if (nowActive == 2)
            {
                allStarted.TrySetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref active);
        }

        var work = Enumerable.Range(0, 4)
            .Select(_ => controller.RunAsync(token => new ValueTask(Work(token))).AsTask())
            .ToArray();

        await allStarted.Task;
        Assert.Equal(2, highestObserved);
        release.SetResult();
        await Task.WhenAll(work);

        Assert.Equal(0, controller.ActiveCount);
    }

    [Fact]
    public async Task RunAsync_releases_a_slot_after_a_failure()
    {
        var controller = BookPageConcurrencyController.Create(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.RunAsync(_ => throw new InvalidOperationException("failure")).AsTask());

        await controller.RunAsync(_ => ValueTask.CompletedTask);
        Assert.Equal(0, controller.ActiveCount);
    }

    [Fact]
    public async Task RunAsync_honours_cancellation_while_waiting_for_a_slot()
    {
        var controller = BookPageConcurrencyController.Create(1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = controller.RunAsync(token => new ValueTask(release.Task.WaitAsync(token))).AsTask();
        using var cancellation = new CancellationTokenSource();

        await Task.Yield();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.RunAsync(_ => ValueTask.CompletedTask, cancellation.Token).AsTask());
        release.SetResult();
        await owner;
        Assert.Equal(0, controller.ActiveCount);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var observed = Volatile.Read(ref location);
            while (value > observed)
            {
                var previous = Interlocked.CompareExchange(ref location, value, observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }
}
