namespace PrintableBook.Core.Application.Execution;

public sealed class ProcessingSessionGate : IProcessingSessionGate
{
    private int running;

    public bool IsRunning => Volatile.Read(ref running) == 1;

    public ValueTask<IProcessingSessionLease?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Interlocked.CompareExchange(ref running, 1, 0) == 0
            ? ValueTask.FromResult<IProcessingSessionLease?>(new ProcessingSessionLease(this))
            : ValueTask.FromResult<IProcessingSessionLease?>(null);
    }

    private void Release() => Interlocked.Exchange(ref running, 0);

    private sealed class ProcessingSessionLease(ProcessingSessionGate gate) : IProcessingSessionLease
    {
        private ProcessingSessionGate? gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
