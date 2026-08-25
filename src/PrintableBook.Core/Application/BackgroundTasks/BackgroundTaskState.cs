namespace PrintableBook.Core.Application.BackgroundTasks;

public enum BackgroundTaskState
{
    Queued,
    Running,
    Cancelling,
    Completed,
    Failed,
    Cancelled
}
