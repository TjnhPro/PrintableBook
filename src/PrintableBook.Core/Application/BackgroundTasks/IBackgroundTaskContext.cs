namespace PrintableBook.Core.Application.BackgroundTasks;

public interface IBackgroundTaskContext
{
    BackgroundTaskId TaskId { get; }

    void Report(
        string step,
        int? completed = null,
        int? total = null,
        string? detail = null,
        string? subject = null);

    void SetView<TView>(TView view)
        where TView : class;
}
