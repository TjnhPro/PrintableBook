using System.Diagnostics;

namespace PrintableBook.Updater;

public sealed class ProcessWaiter : IProcessWaiter
{
    public async ValueTask<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return true; }

        using (process)
        using (var timeoutSource = new CancellationTokenSource(timeout))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
        {
            try
            {
                await process.WaitForExitAsync(linked.Token);
                return true;
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }
}
