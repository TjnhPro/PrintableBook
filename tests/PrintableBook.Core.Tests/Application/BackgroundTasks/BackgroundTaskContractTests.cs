using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Core.Tests.Application.BackgroundTasks;

public sealed class BackgroundTaskContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BackgroundTaskId_rejects_blank_value(string value)
    {
        Assert.Throws<ArgumentException>(() => new BackgroundTaskId(value));
    }

    [Fact]
    public void BackgroundTaskId_New_returns_distinct_nonblank_opaque_ids()
    {
        var first = BackgroundTaskId.New();
        var second = BackgroundTaskId.New();

        Assert.NotEqual(first, second);
        Assert.StartsWith("task-", first.Value);
        Assert.False(string.IsNullOrWhiteSpace(first.Value));
    }

    [Fact]
    public void V1_kinds_and_states_are_canonical()
    {
        Assert.Equal(
            [BackgroundTaskKind.LibraryRefresh, BackgroundTaskKind.ProcessingSession, BackgroundTaskKind.AssetPreview],
            Enum.GetValues<BackgroundTaskKind>());
        Assert.Equal(
            [BackgroundTaskState.Queued, BackgroundTaskState.Running, BackgroundTaskState.Cancelling, BackgroundTaskState.Completed, BackgroundTaskState.Failed, BackgroundTaskState.Cancelled],
            Enum.GetValues<BackgroundTaskState>());
    }
}
