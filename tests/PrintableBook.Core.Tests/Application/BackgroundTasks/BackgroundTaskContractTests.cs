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

    [Fact]
    public void BackgroundTaskSnapshot_carries_only_typed_runtime_fields()
    {
        var snapshot = new BackgroundTaskSnapshot(
            new BackgroundTaskId("task-one"),
            BackgroundTaskKind.LibraryRefresh,
            BackgroundTaskState.Running,
            "library",
            "Library",
            "discovery",
            1,
            2,
            "Discovering Books",
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        Assert.Equal("task-one", snapshot.TaskId.Value);
        Assert.Equal("discovery", snapshot.Step);
        Assert.Equal(2, snapshot.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BackgroundTaskFailureException_rejects_blank_codes(string code)
    {
        Assert.Throws<ArgumentException>(() => new BackgroundTaskFailureException(code, "Safe message"));
    }

    [Fact]
    public void BackgroundTaskFailureException_exposes_a_sanitized_code_and_message()
    {
        var exception = new BackgroundTaskFailureException("library_refresh_failed", "Unable to refresh library.");

        Assert.Equal("library_refresh_failed", exception.Code);
        Assert.Equal("Unable to refresh library.", exception.Message);
    }
}
