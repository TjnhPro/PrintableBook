namespace PrintableBook.Core.Application.BackgroundTasks;

public abstract class BackgroundTaskWorker<TRequest, TResult> : IBackgroundTaskWorker
{
    public abstract BackgroundTaskKind Kind { get; }

    public Type RequestType => typeof(TRequest);

    protected abstract ValueTask<TResult> ExecuteTypedAsync(
        TRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken);

    async ValueTask<object?> IBackgroundTaskWorker.ExecuteAsync(
        object request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        if (request is not TRequest typed)
        {
            throw new ArgumentException(
                $"Worker {Kind} requires request type {typeof(TRequest).Name}.",
                nameof(request));
        }

        return await ExecuteTypedAsync(typed, context, cancellationToken);
    }
}
