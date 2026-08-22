namespace PrintableBook.Core.Application.Execution;

public sealed class BookPageConcurrencyController : IBookPageConcurrencyController
{
    public const int MinimumConcurrency = 1;
    public const int MaximumSupportedConcurrency = 12;

    private readonly SemaphoreSlim semaphore;
    private int activeCount;

    private BookPageConcurrencyController(int maximumConcurrency)
    {
        MaximumConcurrency = maximumConcurrency;
        semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public int MaximumConcurrency { get; }

    public int ActiveCount => Volatile.Read(ref activeCount);

    public static BookPageConcurrencyController Create(int configuredConcurrency) =>
        new(Math.Clamp(configuredConcurrency, MinimumConcurrency, MaximumSupportedConcurrency));

    public async ValueTask RunAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await semaphore.WaitAsync(cancellationToken);
        Interlocked.Increment(ref activeCount);

        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref activeCount);
            semaphore.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        semaphore.Dispose();
        return ValueTask.CompletedTask;
    }
}
