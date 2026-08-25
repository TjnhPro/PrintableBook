using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Desktop.BackgroundTasks;

internal enum BackgroundTaskLaneKind
{
    Library,
    Processing
}

internal enum BackgroundTaskDuplicatePolicy
{
    JoinByKind,
    ReturnExisting
}

internal sealed record BackgroundTaskPolicy(
    BackgroundTaskLaneKind Lane,
    int MaximumConcurrency,
    BackgroundTaskDuplicatePolicy DuplicatePolicy);

internal static class BackgroundTaskPolicies
{
    private static readonly IReadOnlyDictionary<BackgroundTaskKind, BackgroundTaskPolicy> policies =
        new Dictionary<BackgroundTaskKind, BackgroundTaskPolicy>
        {
            [BackgroundTaskKind.LibraryRefresh] = new(BackgroundTaskLaneKind.Library, 1, BackgroundTaskDuplicatePolicy.JoinByKind),
            [BackgroundTaskKind.ProcessingSession] = new(BackgroundTaskLaneKind.Processing, 1, BackgroundTaskDuplicatePolicy.ReturnExisting)
        };

    internal static IReadOnlyDictionary<BackgroundTaskKind, BackgroundTaskPolicy> All => policies;

    internal static BackgroundTaskPolicy For(BackgroundTaskKind kind) => policies.TryGetValue(kind, out var policy)
        ? policy
        : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No background task policy is registered.");
}
