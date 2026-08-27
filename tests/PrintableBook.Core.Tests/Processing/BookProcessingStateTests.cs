using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class BookProcessingStateTests
{
    [Fact]
    public void New_book_defaults_to_brand_background_and_all_interior_active()
    {
        var state = BookProcessingState.NotStarted(new BookId("book"));
        Assert.True(state.HasBackground);
        Assert.True(state.IsInteriorActive("Book interior/page.png"));
        Assert.Null(state.InactiveInteriorSourceKeys);
    }

    [Fact]
    public void SetHasBackground_persists_explicit_choice() =>
        Assert.True(BookProcessingState.NotStarted(new BookId("book")).SetHasBackground(true).HasBackground);

    [Fact]
    public void Intro_defaults_to_automatic_selection_without_persisted_keys()
    {
        var state = BookProcessingState.NotStarted(new BookId("book"));

        Assert.False(state.HasIntro);
        Assert.Null(state.SelectedIntroInteriorSourceKeys);
    }

    [Fact]
    public void SetIntroInteriorSourceKeys_normalizes_and_preserves_explicit_order()
    {
        var state = BookProcessingState.NotStarted(new BookId("book"))
            .SetHasIntro(true)
            .SetIntroInteriorSourceKeys(["Book interior\\page3.png", "Book interior/page1.png"]);

        Assert.True(state.HasIntro);
        Assert.Equal(["Book interior/page3.png", "Book interior/page1.png"], state.SelectedIntroInteriorSourceKeys);
    }

    [Fact]
    public void SetIntroInteriorSourceKeys_allows_an_explicit_empty_custom_selection()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")).SetHasIntro(true).SetIntroInteriorSourceKeys([]);

        Assert.Empty(state.SelectedIntroInteriorSourceKeys!);
    }

    [Fact]
    public void Switching_to_automatic_intro_keeps_the_previous_custom_book_keys_without_using_them()
    {
        var state = BookProcessingState.NotStarted(new BookId("book"))
            .SetHasIntro(true)
            .SetIntroInteriorSourceKeys(["Book interior/page3.png"])
            .SetHasIntro(false);

        Assert.False(state.HasIntro);
        Assert.Equal(["Book interior/page3.png"], state.SelectedIntroInteriorSourceKeys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../page.png")]
    [InlineData("Book interior/../page.png")]
    [InlineData("C:\\outside.png")]
    public void SetIntroInteriorSourceKeys_rejects_nonportable_source_keys(string sourceKey) =>
        Assert.Throws<ArgumentException>(() => BookProcessingState.NotStarted(new BookId("book")).SetIntroInteriorSourceKeys([sourceKey]));

    [Fact]
    public void SetIntroInteriorSourceKeys_rejects_case_insensitive_duplicates() =>
        Assert.Throws<ArgumentException>(() => BookProcessingState.NotStarted(new BookId("book")).SetIntroInteriorSourceKeys(["Book interior/page.png", "book INTERIOR/PAGE.png"]));

    [Fact]
    public void SetInteriorActive_stores_only_inactive_source_keys()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")).SetInteriorActive("Book interior/b.png", false).SetInteriorActive("Book interior/a.png", false);
        Assert.Equal(["Book interior/a.png", "Book interior/b.png"], state.InactiveInteriorSourceKeys);
    }

    [Fact]
    public void SetInteriorActive_reactivate_removes_sparse_override()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")).SetInteriorActive("Book interior/a.png", false).SetInteriorActive("Book interior/a.png", true);
        Assert.Null(state.InactiveInteriorSourceKeys);
    }

    [Fact]
    public void Interior_activation_source_keys_are_case_insensitive()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")).SetInteriorActive("Book interior/A.png", false);
        Assert.False(state.IsInteriorActive("book INTERIOR/a.PNG"));
    }

    [Fact]
    public void SetInteriorActive_does_not_duplicate_equivalent_keys()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")).SetInteriorActive("Book interior/a.png", false).SetInteriorActive("BOOK INTERIOR/A.PNG", false);
        Assert.Single(state.InactiveInteriorSourceKeys!);
    }

    [Fact]
    public void RecordPublishedInteriorPreviews_replaces_the_successful_run_manifest()
    {
        var state = BookProcessingState.NotStarted(new BookId("book"))
            .RecordPublishedInteriorPreviews([new PublishedInteriorPreview("page-0001", "processed/interior/page-0001.png")])
            .RecordPublishedInteriorPreviews([new PublishedInteriorPreview("page-0002", "processed/interior/page-0002.png")]);

        var preview = Assert.Single(state.PublishedInteriorPreviews!);
        Assert.Equal("page-0002", preview.PageId);
    }

    [Fact]
    public void RecordPublishedInteriorPreviews_rejects_duplicate_page_ids()
    {
        var state = BookProcessingState.NotStarted(new BookId("book"));

        Assert.Throws<ArgumentException>(() => state.RecordPublishedInteriorPreviews([
            new PublishedInteriorPreview("page-0001", "processed/interior/page-0001.png"),
            new PublishedInteriorPreview("PAGE-0001", "processed/interior/page-0001-copy.png")
        ]));
    }
    [Fact]
    public void Interior_frame_modes_persist_only_explicit_overrides_immutably()
    {
        var original = BookProcessingState.NotStarted(new BookId("book"));
        var enabled = original.SetInteriorFrameMode("interior/page-001.png", FrameMode.Enabled);
        var disabled = enabled.SetInteriorFrameMode("interior/page-002.png", FrameMode.Disabled);
        var automatic = disabled.SetInteriorFrameMode("interior/page-001.png", FrameMode.Auto);

        Assert.Equal(FrameMode.Auto, original.GetInteriorFrameMode("interior/page-001.png"));
        Assert.Equal(FrameMode.Enabled, enabled.GetInteriorFrameMode("interior/page-001.png"));
        Assert.Equal(FrameMode.Disabled, disabled.GetInteriorFrameMode("INTERIOR/PAGE-002.PNG"));
        Assert.Equal(FrameMode.Auto, automatic.GetInteriorFrameMode("interior/page-001.png"));
        Assert.DoesNotContain("interior/page-001.png", automatic.InteriorFrameOverrides!);
    }

    [Fact]
    public void Interior_frame_modes_reject_blank_keys_and_unknown_modes()
    {
        var state = BookProcessingState.NotStarted(new BookId("book"));
        Assert.Throws<ArgumentException>(() => state.GetInteriorFrameMode(" "));
        Assert.Throws<ArgumentException>(() => state.SetInteriorFrameMode("", FrameMode.Auto));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetInteriorFrameMode("interior/page.png", (FrameMode)99));
    }
    [Fact]
    public void BeginStep_marks_the_active_step_without_marking_it_complete()
    {
        var running = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.Parse("2026-08-22T10:00:00Z"))
            .BeginStep("scan", DateTimeOffset.Parse("2026-08-22T10:00:30Z"))
            .CompleteStep("scan", DateTimeOffset.Parse("2026-08-22T10:00:45Z"))
            .BeginStep("interior-pages", DateTimeOffset.Parse("2026-08-22T10:01:00Z"));

        Assert.Equal(BookProcessingStatus.Running, running.Status);
        Assert.Equal("interior-pages", running.CurrentStep);
        Assert.Equal("scan", running.LastCompletedStep);
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
            .BeginStep("resize", DateTimeOffset.UtcNow)
            .Cancel(DateTimeOffset.UtcNow);

        Assert.Equal(BookProcessingStatus.Cancelled, cancelled.Status);
        Assert.Equal("resize", cancelled.CurrentStep);
        Assert.Equal("canvas", cancelled.LastCompletedStep);
        Assert.True(cancelled.MayResume);
    }

    [Fact]
    public void Interruption_preserves_workspace_progress_for_recovery()
    {
        var running = BookProcessingState.NotStarted(new BookId("book-one"))
            .Start(DateTimeOffset.Parse("2026-08-22T10:00:00Z"))
            .CompleteStep("trim", DateTimeOffset.Parse("2026-08-22T10:01:00Z"))
            .BeginStep("resize", DateTimeOffset.Parse("2026-08-22T10:02:00Z"));

        var interrupted = running.Interrupt(DateTimeOffset.Parse("2026-08-22T10:03:00Z"));

        Assert.Equal(BookProcessingStatus.Interrupted, interrupted.Status);
        Assert.True(interrupted.MayResume);
        Assert.Equal("resize", interrupted.CurrentStep);
        Assert.Equal("trim", interrupted.LastCompletedStep);
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
