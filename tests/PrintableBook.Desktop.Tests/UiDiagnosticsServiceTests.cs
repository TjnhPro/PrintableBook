using PrintableBook.Desktop.Diagnostics;

namespace PrintableBook.Desktop.Tests;

public sealed class UiDiagnosticsServiceTests
{
    [Fact]
    public void Fast_operations_do_not_create_noise_and_completed_operations_leave_active_stall_context()
    {
        var clock = new TestClock();
        var diagnostics = new UiDiagnosticsService(clock.Now);
        using (diagnostics.Begin("book.scan", "Book One")) clock.Advance(200);

        diagnostics.RecordDispatcherStall(TimeSpan.FromMilliseconds(300));

        var stall = Assert.Single(diagnostics.Snapshot());
        Assert.Equal(UiDiagnosticSeverity.Slow, stall.Severity);
        Assert.Empty(stall.ActiveOperations!);
    }

    [Fact]
    public void Slow_and_severe_operations_are_classified_without_sleeping()
    {
        var clock = new TestClock();
        var diagnostics = new UiDiagnosticsService(clock.Now);
        using (diagnostics.Begin("book.scan")) clock.Advance(300);
        using (diagnostics.Begin("image.inspect")) clock.Advance(1000);

        var events = diagnostics.Snapshot();
        Assert.Equal([UiDiagnosticSeverity.Slow, UiDiagnosticSeverity.Severe], events.Select(item => item.Severity));
    }

    [Fact]
    public void Buffer_keeps_the_newest_two_hundred_events_and_stall_captures_active_operations()
    {
        var clock = new TestClock();
        var diagnostics = new UiDiagnosticsService(clock.Now);
        using var active = diagnostics.Begin("book.scan", "Book One");
        diagnostics.RecordDispatcherStall(TimeSpan.FromMilliseconds(1200));
        for (var index = 0; index < 205; index++) diagnostics.RecordDispatcherStall(TimeSpan.FromMilliseconds(300));

        var events = diagnostics.Snapshot();
        Assert.Equal(200, events.Count);
        Assert.DoesNotContain(events, item => item.Severity == UiDiagnosticSeverity.Severe);
        Assert.Equal("book.scan (Book One)", diagnostics.Snapshot().Last().ActiveOperations!.Single());
    }

    private sealed class TestClock
    {
        private DateTimeOffset current = DateTimeOffset.UnixEpoch;
        public DateTimeOffset Now() => current;
        public void Advance(int milliseconds) => current = current.AddMilliseconds(milliseconds);
    }
}
