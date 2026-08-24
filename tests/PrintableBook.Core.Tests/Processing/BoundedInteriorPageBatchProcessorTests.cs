using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Processing;

public sealed class BoundedInteriorPageBatchProcessorTests
{
    [Fact]
    public async Task ProcessAsync_uses_the_shared_controller_and_returns_results_in_input_order()
    {
        var pipeline = new ControllablePipeline();
        var requests = new[]
        {
            Request("page-01"),
            Request("page-02"),
            Request("page-03")
        };
        await using var controller = BookPageConcurrencyController.Create(2);

        var results = await new BoundedInteriorPageBatchProcessor(pipeline).ProcessAsync(requests, controller);

        Assert.Equal(2, pipeline.MaximumObservedConcurrency);
        Assert.Equal(requests.Select(request => request.PageId), results.Select(result => result.PageId));
    }

    private static InteriorPagePipelineRequest Request(string pageId) => new(
        new BookWorkspace(new BookId("book-one"), new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("output-temp")),
        new FileReference($"{pageId}.png"),
        pageId,
        new ArtworkDetectionThreshold(20),
        new ImageSize(100, 100),
        new ImageSize(120, 120),
        new ImageSize(140, 150),
        new ImageDensity(300, 300),
        null,
        false);

    private sealed class ControllablePipeline : IInteriorPagePipeline
    {
        private int active;
        private int maximumObservedConcurrency;
        private readonly TaskCompletionSource twoOperationsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumObservedConcurrency => Volatile.Read(ref maximumObservedConcurrency);

        public async ValueTask<InteriorPageProcessingResult> ProcessAsync(InteriorPagePipelineRequest request, CancellationToken cancellationToken = default)
        {
            var activeNow = Interlocked.Increment(ref active);
            UpdateMaximum(activeNow);
            try
            {
                if (activeNow == 2)
                {
                    twoOperationsStarted.TrySetResult();
                }

                await twoOperationsStarted.Task.WaitAsync(cancellationToken);
                return new InteriorPageProcessingResult(request.PageId, request.Source, new FileReference($"output/{request.PageId}.png"));
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            while (candidate > Volatile.Read(ref maximumObservedConcurrency))
            {
                if (Interlocked.CompareExchange(ref maximumObservedConcurrency, candidate, Volatile.Read(ref maximumObservedConcurrency)) >= candidate)
                {
                    return;
                }
            }
        }
    }
}
