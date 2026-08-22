using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class BookProcessingQueueProcessorTests
{
    [Fact]
    public async Task ProcessAsync_rejects_a_second_queue_while_the_first_queue_owns_the_session_gate()
    {
        var gate = new ProcessingSessionGate();
        var bookProcessor = new BlockingBookProcessor();
        var processor = new BookProcessingQueueProcessor(
            gate,
            bookProcessor);
        var request = new BookProcessingQueueRequest([Command("book-one")]);

        var firstRun = processor.ProcessAsync(request).AsTask();
        await bookProcessor.WaitUntilStartedAsync();
        var secondRun = await processor.ProcessAsync(request);

        Assert.True(secondRun.IsAlreadyRunning);
        await bookProcessor.ReleaseAsync();
        var firstResult = await firstRun;
        Assert.False(firstResult.IsAlreadyRunning);
    }

    [Fact]
    public async Task ProcessAsync_runs_books_in_order_and_continues_after_a_failed_book()
    {
        var bookProcessor = new RecordingBookProcessor();
        var processor = new BookProcessingQueueProcessor(new ProcessingSessionGate(), bookProcessor);

        var result = await processor.ProcessAsync(new BookProcessingQueueRequest([Command("failed-book"), Command("next-book")]));

        Assert.Equal(["failed-book", "next-book"], bookProcessor.ProcessedBookIds);
        Assert.Equal([BookProcessingStatus.Failed, BookProcessingStatus.Completed], result.Books.Select(book => book.Status));
    }

    private static PrintableBookProcessingCommand Command(string bookId) => new(
        new BookId(bookId),
        new DirectoryReference(bookId),
        new DirectoryReference("final"),
        new ImageSize(100, 100),
        new ImageSize(100, 100),
        new ImageDensity(300, 300),
        new PhysicalPageSize(8.5, 8.5),
        1,
        new ArtworkDetectionThreshold(20),
        null,
        false,
        1);

    private sealed class BlockingBookProcessor : IBookProcessingQueueBookProcessor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<BookProcessingQueueBookResult> ProcessBookAsync(PrintableBookProcessingCommand command, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return BookProcessingQueueBookResult.Completed(command.BookId, null);
        }

        public Task WaitUntilStartedAsync() => started.Task;

        public Task ReleaseAsync() => release.TrySetResult() ? Task.CompletedTask : Task.CompletedTask;
    }

    private sealed class RecordingBookProcessor : IBookProcessingQueueBookProcessor
    {
        public List<string> ProcessedBookIds { get; } = [];

        public ValueTask<BookProcessingQueueBookResult> ProcessBookAsync(PrintableBookProcessingCommand command, CancellationToken cancellationToken = default)
        {
            ProcessedBookIds.Add(command.BookId.Value);
            return ValueTask.FromResult(command.BookId.Value == "failed-book"
                ? new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Failed, new ProcessingFailure("test.failure", "Expected failure."), null)
                : BookProcessingQueueBookResult.Completed(command.BookId, null));
        }
    }
}
