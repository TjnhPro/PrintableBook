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
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public ValueTask<ProcessWindowCloseOutcome> RequestCloseAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(ProcessShutdownIntent.ExitApplication, cancellationToken);

    public ValueTask<ProcessWindowCloseOutcome> RequestUpdateRestartAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(ProcessShutdownIntent.RestartForUpdate, cancellationToken);

    private async ValueTask<ProcessWindowCloseOutcome> RequestAsync(ProcessShutdownIntent intent, CancellationToken cancellationToken)
    {
        var current = await processSessionService.GetAsync(cancellationToken);
        if (!current.IsActive) return ProcessWindowCloseOutcome.Close;
        if (prompt.ConfirmActiveProcessClose(intent) == ActiveProcessCloseDecision.ContinueUsingApp)
        {
            return ProcessWindowCloseOutcome.KeepOpen;
        }

        while (true)
        {
            if (await processSessionService.StopAndWaitAsync(StopTimeout, cancellationToken))
            {
                return ProcessWindowCloseOutcome.Close;
            }

            if (prompt.ConfirmStopTimeout(intent) == ProcessStopTimeoutDecision.ForceExit)
            {
                return ProcessWindowCloseOutcome.ForceExit;
            }
        }
    }
}
