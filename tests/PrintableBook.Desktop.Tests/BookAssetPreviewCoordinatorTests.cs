using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Desktop.Preview;

namespace PrintableBook.Desktop.Tests;

public sealed class BookAssetPreviewCoordinatorTests
{
    [Fact]
    public async Task GetAsync_limits_backend_preview_work_to_two_concurrent_calls()
    {
        var preview = new BlockingPreviewService();
        var coordinator = new BookAssetPreviewCoordinator(preview, new NoOpOperationDiagnostics());

        var first = coordinator.GetAsync("Book", "one.png").AsTask();
        var second = coordinator.GetAsync("Book", "two.png").AsTask();
        var third = coordinator.GetAsync("Book", "three.png").AsTask();
        await preview.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, preview.MaximumConcurrent);
        preview.Release.TrySetResult();
        await Task.WhenAll(first, second, third);
        Assert.Equal(2, preview.MaximumConcurrent);
    }

    [Fact]
    public async Task GetAsync_cancels_while_waiting_for_a_slot_without_calling_the_preview_service()
    {
        var preview = new BlockingPreviewService();
        var coordinator = new BookAssetPreviewCoordinator(preview, new NoOpOperationDiagnostics());
        _ = coordinator.GetAsync("Book", "one.png").AsTask();
        _ = coordinator.GetAsync("Book", "two.png").AsTask();
        await preview.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        var queued = coordinator.GetAsync("Book", "three.png", cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(2, preview.Calls);
        preview.Release.TrySetResult();
    }

    private sealed class BlockingPreviewService : IBookAssetPreviewService
    {
        private int concurrent;
        public int Calls { get; private set; }
        public int MaximumConcurrent { get; private set; }
        public TaskCompletionSource TwoStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<BookAssetPreview?> GetAsync(string bookId, string sourceReference, CancellationToken cancellationToken = default)
        {
            Calls++;
            var now = Interlocked.Increment(ref concurrent);
            MaximumConcurrent = Math.Max(MaximumConcurrent, now);
            if (now == 2) TwoStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrent);
            return new BookAssetPreview(bookId, sourceReference, 1, 1, "data:image/png;base64,preview");
        }
    }
}
