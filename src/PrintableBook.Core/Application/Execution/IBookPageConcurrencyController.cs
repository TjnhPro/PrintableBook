namespace PrintableBook.Core.Application.Execution;

/// <summary>
/// Bounds concurrent page pipelines for one book. Individual processors must not create limits of their own.
/// </summary>
public interface IBookPageConcurrencyController : IAsyncDisposable
{
    int MaximumConcurrency { get; }

    int ActiveCount { get; }

    ValueTask RunAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);
}
