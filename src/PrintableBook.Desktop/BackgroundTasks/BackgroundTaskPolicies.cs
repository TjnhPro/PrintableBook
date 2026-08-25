using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Desktop.BackgroundTasks;

internal enum BackgroundTaskLaneKind
{
    Library,
    Processing,
    Cleanup
}

internal enum BackgroundTaskDuplicatePolicy
{
    JoinByKind,
    ReturnExisting
}

internal sealed record BackgroundTaskPolicy(
    BackgroundTaskLaneKind Lane,
    int MaximumConcurrency,
    BackgroundTaskDuplicatePolicy DuplicatePolicy,
    IReadOnlyList<BackgroundTaskKind> Conflicts);

internal static class BackgroundTaskPolicies
{
    private static readonly IReadOnlyDictionary<BackgroundTaskKind, BackgroundTaskPolicy> policies =
        new Dictionary<BackgroundTaskKind, BackgroundTaskPolicy>
        {
            [BackgroundTaskKind.LibraryRefresh] = new(
                BackgroundTaskLaneKind.Library,
                1,
                BackgroundTaskDuplicatePolicy.JoinByKind,
                [BackgroundTaskKind.CacheCleanup]),
            [BackgroundTaskKind.ProcessingSession] = new(
                BackgroundTaskLaneKind.Processing,
                1,
                BackgroundTaskDuplicatePolicy.ReturnExisting,
                [BackgroundTaskKind.CacheCleanup]),
            [BackgroundTaskKind.CacheCleanup] = new(
                BackgroundTaskLaneKind.Cleanup,
                1,
                BackgroundTaskDuplicatePolicy.ReturnExisting,
                [BackgroundTaskKind.LibraryRefresh, BackgroundTaskKind.ProcessingSession])
        };

    internal static IReadOnlyDictionary<BackgroundTaskKind, BackgroundTaskPolicy> All => policies;

    internal static BackgroundTaskPolicy For(BackgroundTaskKind kind) => policies.TryGetValue(kind, out var policy)
        ? policy
        : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No background task policy is registered.");
}
