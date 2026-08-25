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

    public void Start() => monitorTask ??= Task.Run(MonitorAsync, CancellationToken.None);

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
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
