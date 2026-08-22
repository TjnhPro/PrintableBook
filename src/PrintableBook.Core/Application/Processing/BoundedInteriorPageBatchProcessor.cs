using PrintableBook.Core.Application.Execution;

namespace PrintableBook.Core.Application.Processing;

public sealed class BoundedInteriorPageBatchProcessor(IInteriorPagePipeline pagePipeline)
{
    public async ValueTask<IReadOnlyList<InteriorPageProcessingResult>> ProcessAsync(
        IReadOnlyList<InteriorPagePipelineRequest> requests,
        IBookPageConcurrencyController concurrencyController,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(concurrencyController);
        if (requests.Count == 0)
        {
            return Array.Empty<InteriorPageProcessingResult>();
        }

        using var remainingWorkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var results = new InteriorPageProcessingResult?[requests.Count];
        var work = requests.Select((request, index) => ProcessOneAsync(request, index)).ToArray();

        try
        {
            await Task.WhenAll(work);
        }
        catch
        {
            await remainingWorkCancellation.CancelAsync();
            try
            {
                await Task.WhenAll(work);
            }
            catch
            {
                // The original exception is retained for the caller.
            }

            throw;
        }

        return results.Select(result => result!).ToArray();

        async Task ProcessOneAsync(InteriorPagePipelineRequest request, int index)
        {
            await concurrencyController.RunAsync(async token =>
            {
                results[index] = await pagePipeline.ProcessAsync(request, token);
            }, remainingWorkCancellation.Token);
        }
    }
}
