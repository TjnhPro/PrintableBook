namespace PrintableBook.Core.Application.Execution;

/// <summary>
/// Prevents overlapping processing queues in an application host.
/// </summary>
public interface IProcessingSessionGate
{
    bool IsRunning { get; }

    ValueTask<IProcessingSessionLease?> TryAcquireAsync(CancellationToken cancellationToken = default);
}
