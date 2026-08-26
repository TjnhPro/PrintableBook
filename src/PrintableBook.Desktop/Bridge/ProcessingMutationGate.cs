namespace PrintableBook.Desktop.Bridge;

internal sealed class ProcessingMutationGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
