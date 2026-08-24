using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Desktop;

public enum ProcessWindowCloseOutcome
{
    KeepOpen,
    Close,
    ForceExit
}

public sealed class ProcessWindowShutdownCoordinator(
    IProcessSessionService processSessionService,
    IProcessShutdownPrompt prompt)
{
    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public async ValueTask<ProcessWindowCloseOutcome> RequestCloseAsync(CancellationToken cancellationToken = default)
    {
        var current = await processSessionService.GetAsync(cancellationToken);
        if (!current.IsActive) return ProcessWindowCloseOutcome.Close;
        if (prompt.ConfirmActiveProcessClose() == ActiveProcessCloseDecision.ContinueUsingApp)
        {
            return ProcessWindowCloseOutcome.KeepOpen;
        }

        while (true)
        {
            if (await processSessionService.StopAndWaitAsync(StopTimeout, cancellationToken))
            {
                return ProcessWindowCloseOutcome.Close;
            }

            if (prompt.ConfirmStopTimeout() == ProcessStopTimeoutDecision.ForceExit)
            {
                return ProcessWindowCloseOutcome.ForceExit;
            }
        }
    }
}
