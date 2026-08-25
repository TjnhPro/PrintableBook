using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Desktop.BackgroundTasks;

namespace PrintableBook.Desktop.Tests.BackgroundTasks;

public sealed class BackgroundTaskManagerTests
{
    [Fact]
    public void V1_policies_define_exactly_the_three_supported_task_kinds_and_lane_limits()
    {
        Assert.Equal(
            [BackgroundTaskKind.LibraryRefresh, BackgroundTaskKind.ProcessingSession, BackgroundTaskKind.AssetPreview],
            BackgroundTaskPolicies.All.Keys.Order());

        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Library, 1, BackgroundTaskDuplicatePolicy.JoinByKind), BackgroundTaskPolicies.For(BackgroundTaskKind.LibraryRefresh));
        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Processing, 1, BackgroundTaskDuplicatePolicy.ReturnExisting), BackgroundTaskPolicies.For(BackgroundTaskKind.ProcessingSession));
        Assert.Equal(new BackgroundTaskPolicy(BackgroundTaskLaneKind.Preview, 2, BackgroundTaskDuplicatePolicy.JoinByKey), BackgroundTaskPolicies.For(BackgroundTaskKind.AssetPreview));
    }
}
