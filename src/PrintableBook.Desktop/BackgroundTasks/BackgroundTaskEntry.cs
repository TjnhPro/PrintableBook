using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Desktop.BackgroundTasks;

internal sealed class BackgroundTaskEntry
{
    public required BackgroundTaskId TaskId { get; init; }
    public required BackgroundTaskKind Kind { get; init; }
    public required string Key { get; init; }
    public string? Subject { get; set; }
    public required object Request { get; init; }
    public BackgroundTaskState State { get; set; }
    public string? Step { get; set; }
    public int? Completed { get; set; }
    public int? Total { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public object? View { get; set; }
    public object? Result { get; set; }
    public required CancellationTokenSource Cancellation { get; init; }
    public object CancellationSync { get; } = new();
    public Task CancellationSignalTask { get; set; } = Task.CompletedTask;
    public Task? ExecutionTask { get; set; }
    public TaskCompletionSource Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public long Sequence { get; init; }
}
