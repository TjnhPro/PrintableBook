using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Commands;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Results;
using PrintableBook.Core.Configuration;
using PrintableBook.Core.Domain.Brands;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Application;

public sealed class BookProcessingPipelineTests
{
    [Fact]
    public async Task ProcessAsync_stops_at_the_first_terminal_stage_result()
    {
        var pipeline = new BookProcessingPipeline(
        [
            new ReturningStage(null),
            new ReturningStage(ProcessingResult.Failed(new ProcessingIssue("asset.unreadable", "Cannot read asset."))),
            new ReturningStage(ProcessingResult.Succeeded())
        ]);

        var result = await pipeline.ProcessAsync(CreateRequest());

        Assert.Equal(ProcessingStatus.Failure, result.Status);
    }

    [Fact]
    public async Task ProcessAsync_honours_a_pre_cancelled_token_when_no_stages_are_registered()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var pipeline = new BookProcessingPipeline([]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ProcessAsync(CreateRequest(), cancellationToken: cancellation.Token).AsTask());
    }

    private static ProcessingRequest CreateRequest() => new(
        new ProcessingContext(
            new Book(new BookId("book-one"), new BookSource([])),
            new BrandProfile(new BrandId("brand-one")),
            new EffectiveProcessingSettings(new Dictionary<string, string?>()),
            new BookWorkspace(new BookId("book-one"), new DirectoryReference("work"), new DirectoryReference("output")),
            ProcessingOptions.Empty));

    private sealed class ReturningStage(ProcessingResult? result) : IBookProcessingStage
    {
        public string Name => "test";

        public ValueTask<ProcessingResult?> ProcessAsync(
            ProcessingContext context,
            IProgress<PrintableBook.Core.Application.Progress.ProcessingProgress>? progress = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }
}
