using PrintableBook.Core.Application.BackgroundTasks;

namespace PrintableBook.Desktop.BackgroundTasks;

internal sealed class BackgroundTaskContext(
    BackgroundTaskId taskId,
    Action<BackgroundTaskId, string, int?, int?, string?, string?> report,
    Action<BackgroundTaskId, object> setView) : IBackgroundTaskContext
{
    public BackgroundTaskId TaskId { get; } = taskId;

    public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        if (completed is < 0) throw new ArgumentOutOfRangeException(nameof(completed));
        if (total is <= 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (completed.HasValue && total.HasValue && completed > total) throw new ArgumentOutOfRangeException(nameof(completed));

        report(TaskId, step, completed, total, detail, subject);
    }

    public void SetView<TView>(TView view) where TView : class
    {
        ArgumentNullException.ThrowIfNull(view);
        setView(TaskId, view);
    }
}
