namespace PrintableBook.Core.Application.BackgroundTasks;

public interface IBackgroundTaskWorker
{
    BackgroundTaskKind Kind { get; }

    Type RequestType { get; }

    ValueTask<object?> ExecuteAsync(
        object request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken);
}
