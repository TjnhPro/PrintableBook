using System.Diagnostics;
using System.Windows.Threading;

namespace PrintableBook.Desktop.Diagnostics;

public sealed class DispatcherStallMonitor(Dispatcher dispatcher, UiDiagnosticsService diagnostics) : IDisposable
{
    internal static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan SevereThreshold = TimeSpan.FromMilliseconds(1000);
    private readonly CancellationTokenSource cancellation = new();
    private Task? monitorTask;
    private int disposed;

    public void Start()
    {
        if (Volatile.Read(ref disposed) != 0) return;

        monitorTask ??= Task.Run(MonitorAsync, CancellationToken.None);
        _ = monitorTask.ContinueWith(static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal void RecordLatency(TimeSpan duration)
    {
        if (duration >= SlowThreshold) diagnostics.RecordDispatcherStall(duration);
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(ProbeInterval, cancellation.Token).ConfigureAwait(false);
                var started = Stopwatch.GetTimestamp();
                await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background).Task.ConfigureAwait(false);
                RecordLatency(Stopwatch.GetElapsedTime(started));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Normal window shutdown.
        }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted)
        {
            // WPF has already stopped accepting work during application shutdown.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        cancellation.Cancel();
        cancellation.Dispose();
    }
}
