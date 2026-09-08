namespace PrintableBook.Updater;

public interface IProcessWaiter
{
    ValueTask<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default);
}
