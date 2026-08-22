using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class BookProcessingStateTests
{
    [Fact]
    public void BeginStep_marks_the_active_step_without_marking_it_complete()
    {
        var running = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.Parse("2026-08-22T10:00:00Z"))
            .BeginStep("interior-pages", DateTimeOffset.Parse("2026-08-22T10:01:00Z"));

        Assert.Equal(BookProcessingStatus.Running, running.Status);
        Assert.Equal("interior-pages", running.CurrentStep);
        Assert.Null(running.LastCompletedStep);
    }

    [Fact]
    public void CompleteStep_clears_the_active_step_after_work_finishes()
    {
        var completedStep = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.Parse("2026-08-22T10:00:00Z"))
            .BeginStep("interior-pages", DateTimeOffset.Parse("2026-08-22T10:01:00Z"))
            .CompleteStep("interior-pages", DateTimeOffset.Parse("2026-08-22T10:02:00Z"));

        Assert.Null(completedStep.CurrentStep);
        Assert.Equal("interior-pages", completedStep.LastCompletedStep);
    }

    [Fact]
    public void Failure_records_the_failed_step_reason_and_a_resumable_state()
    {
        var started = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.Parse("2026-08-22T10:00:00Z"))
            .CompleteStep("trim", DateTimeOffset.Parse("2026-08-22T10:01:00Z"));

        var failed = started.Fail(
            "resize",
            new ProcessingFailure("image.resize_failed", "Target dimensions are invalid."),
            DateTimeOffset.Parse("2026-08-22T10:02:00Z"));

        Assert.Equal(BookProcessingStatus.Failed, failed.Status);
        Assert.Equal("trim", failed.LastCompletedStep);
        Assert.Equal("resize", failed.FailedStep);
        Assert.Equal("image.resize_failed", failed.Failure!.Code);
        Assert.True(failed.MayResume);
    }

    [Fact]
    public void Cancellation_preserves_completed_work_for_a_later_retry()
    {
        var cancelled = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.UtcNow)
            .CompleteStep("canvas", DateTimeOffset.UtcNow)
            .Cancel(DateTimeOffset.UtcNow);

        Assert.Equal(BookProcessingStatus.Cancelled, cancelled.Status);
        Assert.Equal("canvas", cancelled.LastCompletedStep);
        Assert.True(cancelled.MayResume);
    }

    [Fact]
    public void Completion_is_not_resumable()
    {
        var completed = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.UtcNow)
            .CompleteStep("publish", DateTimeOffset.UtcNow)
            .Complete(DateTimeOffset.UtcNow);

        Assert.Equal(BookProcessingStatus.Completed, completed.Status);
        Assert.False(completed.MayResume);
    }
}
