using PrintableBook.Desktop.Diagnostics;
using System.Windows.Threading;

namespace PrintableBook.Desktop.Tests;

public sealed class DispatcherStallMonitorTests
{
    [Fact]
    public void RecordLatency_ignores_fast_probes_and_classifies_slow_and_severe_stalls()
    {
        var diagnostics = new UiDiagnosticsService();
        using var monitor = new DispatcherStallMonitor(Dispatcher.CurrentDispatcher, diagnostics);

        monitor.RecordLatency(TimeSpan.FromMilliseconds(200));
        monitor.RecordLatency(TimeSpan.FromMilliseconds(300));
        monitor.RecordLatency(TimeSpan.FromMilliseconds(1200));

        Assert.Equal([UiDiagnosticSeverity.Slow, UiDiagnosticSeverity.Severe], diagnostics.Snapshot().Select(item => item.Severity));
    }

    [Fact]
    public void Dispose_is_idempotent_when_the_window_and_service_provider_both_release_the_singleton()
    {
        var monitor = new DispatcherStallMonitor(Dispatcher.CurrentDispatcher, new UiDiagnosticsService());

        monitor.Dispose();
        monitor.Dispose();
    }
}
