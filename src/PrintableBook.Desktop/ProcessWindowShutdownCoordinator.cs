using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.AmazonCrawl;

namespace PrintableBook.Desktop;

public enum ProcessWindowCloseOutcome
{
    KeepOpen,
    Close,
    ForceExit
}

public sealed class ProcessWindowShutdownCoordinator(
    IProcessSessionService processSessionService,
    IProcessShutdownPrompt prompt,
    IAmazonAsinCrawlSessionService? amazonCrawlSessionService = null,
    IAmazonBrowserLifetime? amazonBrowserLifetime = null)
{
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public ValueTask<ProcessWindowCloseOutcome> RequestCloseAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(ProcessShutdownIntent.ExitApplication, cancellationToken);

    public ValueTask<ProcessWindowCloseOutcome> RequestUpdateRestartAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(ProcessShutdownIntent.RestartForUpdate, cancellationToken);

    private async ValueTask<ProcessWindowCloseOutcome> RequestAsync(ProcessShutdownIntent intent, CancellationToken cancellationToken)
    {
        var current = await processSessionService.GetAsync(cancellationToken);
        if (!current.IsActive)
        {
            await StopAmazonAsync(cancellationToken);
            return ProcessWindowCloseOutcome.Close;
        }
        if (prompt.ConfirmActiveProcessClose(intent) == ActiveProcessCloseDecision.ContinueUsingApp)
        {
            return ProcessWindowCloseOutcome.KeepOpen;
        }

        while (true)
        {
            if (await processSessionService.StopAndWaitAsync(StopTimeout, cancellationToken))
            {
                await StopAmazonAsync(cancellationToken);
                return ProcessWindowCloseOutcome.Close;
            }

            if (prompt.ConfirmStopTimeout(intent) == ProcessStopTimeoutDecision.ForceExit)
            {
                return ProcessWindowCloseOutcome.ForceExit;
            }
        }
    }

    private async ValueTask StopAmazonAsync(CancellationToken cancellationToken)
    {
        if (amazonCrawlSessionService is not null)
        {
            await amazonCrawlSessionService.StopAndWaitAsync(StopTimeout, cancellationToken);
        }
        if (amazonBrowserLifetime is not null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StopTimeout);
            await amazonBrowserLifetime.ShutdownAsync(timeout.Token);
        }
    }
}
