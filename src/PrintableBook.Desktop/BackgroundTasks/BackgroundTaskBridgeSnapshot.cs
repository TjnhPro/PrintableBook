using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Desktop.BackgroundTasks;

internal sealed record BackgroundTaskBridgeSnapshot(
    string TaskId,
    string Kind,
    string State,
    string Key,
    string? Subject,
    string? Step,
    int? Completed,
    int? Total,
    string? Detail,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static BackgroundTaskBridgeSnapshot From(BackgroundTaskSnapshot snapshot) => new(
        snapshot.TaskId.Value,
        snapshot.Kind.ToString(),
        snapshot.State.ToString(),
        snapshot.Key,
        snapshot.Subject,
        snapshot.Step,
        snapshot.Completed,
        snapshot.Total,
        snapshot.Detail,
        snapshot.StartedAt,
        snapshot.FinishedAt,
        snapshot.ErrorCode,
        snapshot.ErrorMessage);
}
