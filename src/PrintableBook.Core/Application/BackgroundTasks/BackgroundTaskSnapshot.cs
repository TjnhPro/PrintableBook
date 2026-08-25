namespace PrintableBook.Core.Application.BackgroundTasks;

public sealed record BackgroundTaskSnapshot(
    BackgroundTaskId TaskId,
    BackgroundTaskKind Kind,
    BackgroundTaskState State,
    string Key,
    string? Subject,
    string? Step,
    int? Completed,
    int? Total,
    string? Detail,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorCode,
    string? ErrorMessage);
