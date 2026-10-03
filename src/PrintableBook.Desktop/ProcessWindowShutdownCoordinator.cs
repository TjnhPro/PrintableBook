using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.BackgroundTasks;

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
    IAmazonBrowserLifetime? amazonBrowserLifetime = null,
    IBackgroundTaskManager? backgroundTaskManager = null)
{
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public ValueTask<ProcessWindowCloseOutcome> RequestCloseAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(ProcessShutdownIntent.ExitApplication, cancellationToken);

    public ValueTask<ProcessWindowCloseOutcome> RequestUpdateRestartAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(ProcessShutdownIntent.RestartForUpdate, cancellationToken);

    private async ValueTask<ProcessWindowCloseOutcome> RequestAsync(ProcessShutdownIntent intent, CancellationToken cancellationToken)
    {
        var current = await processSessionService.GetAsync(cancellationToken);
        var storageActive = await HasActiveStorageAsync(cancellationToken);
        if (!current.IsActive && !storageActive)
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
            var processStop = current.IsActive
                ? processSessionService.StopAndWaitAsync(StopTimeout, cancellationToken).AsTask()
                : Task.FromResult(true);
            var storageStop = StopStorageAndWaitAsync(StopTimeout, cancellationToken).AsTask();
            await Task.WhenAll(processStop, storageStop);
            if (await processStop && await storageStop)
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

    public async ValueTask<bool> StopStorageAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (backgroundTaskManager is null) return true;
        var active = (await backgroundTaskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken))
            .Where(task => task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling)
            .ToArray();
        if (active.Length == 0) return true;
        foreach (var task in active)
            await backgroundTaskManager.CancelAsync(task.TaskId, cancellationToken);
        var waits = active.Select(task => backgroundTaskManager.WaitAsync(task.TaskId, timeout, cancellationToken).AsTask()).ToArray();
        return (await Task.WhenAll(waits)).All(completed => completed);
    }

    private async ValueTask<bool> HasActiveStorageAsync(CancellationToken cancellationToken)
    {
        if (backgroundTaskManager is null) return false;
        return (await backgroundTaskManager.ListAsync(BackgroundTaskKind.S3Storage, cancellationToken))
            .Any(task => task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling);
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
