using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Desktop.BackgroundTasks;

internal sealed class BackgroundTaskLane(int maximumConcurrency)
{
    internal Queue<BackgroundTaskId> Queue { get; } = [];
    internal int ActiveCount { get; set; }
    internal int MaximumConcurrency { get; } = maximumConcurrency > 0
        ? maximumConcurrency
        : throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
}
