using System.Diagnostics;
using PrintableBook.Core.Application.Diagnostics;

namespace PrintableBook.Desktop.Diagnostics;

internal enum UiDiagnosticSeverity
{
    Info,
    Slow,
    Severe
}

internal sealed record UiDiagnosticEvent(
    DateTimeOffset Timestamp,
    string Kind,
    UiDiagnosticSeverity Severity,
    string Operation,
    long DurationMilliseconds,
    string? Subject,
    IReadOnlyList<string>? ActiveOperations = null);

internal sealed class UiDiagnosticsService(Func<DateTimeOffset>? clock = null) : IOperationDiagnostics
{
    private const int MaximumEvents = 200;
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SevereThreshold = TimeSpan.FromMilliseconds(1000);
    private readonly Lock sync = new();
    private readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<Guid, ActiveOperation> active = [];
    private readonly Queue<UiDiagnosticEvent> events = [];

    public IDisposable Begin(string operation, string? subject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var id = Guid.NewGuid();
        lock (sync)
        {
            active.Add(id, new ActiveOperation(operation, subject, clock()));
        }

        return new Scope(this, id);
    }

    public void RecordDispatcherStall(TimeSpan duration)
    {
        if (duration < SlowThreshold) return;
        lock (sync)
        {
            Record(new UiDiagnosticEvent(clock(), "dispatcher.stall", SeverityFor(duration), "dispatcher", (long)duration.TotalMilliseconds, null, active.Values.Select(Describe).ToArray()));
        }
    }

    public IReadOnlyList<UiDiagnosticEvent> Snapshot()
    {
        lock (sync) return events.ToArray();
    }

    private void Complete(Guid id)
    {
        lock (sync)
        {
            if (!active.Remove(id, out var operation)) return;
            var duration = clock() - operation.StartedAt;
            if (duration >= SlowThreshold)
            {
                Record(new UiDiagnosticEvent(clock(), "operation", SeverityFor(duration), operation.Name, (long)duration.TotalMilliseconds, operation.Subject));
            }
        }
    }

    private void Record(UiDiagnosticEvent item)
    {
        events.Enqueue(item);
        while (events.Count > MaximumEvents) events.Dequeue();
    }

    private static UiDiagnosticSeverity SeverityFor(TimeSpan duration) => duration >= SevereThreshold ? UiDiagnosticSeverity.Severe : UiDiagnosticSeverity.Slow;
    private static string Describe(ActiveOperation operation) => string.IsNullOrWhiteSpace(operation.Subject) ? operation.Name : $"{operation.Name} ({operation.Subject})";
    private sealed record ActiveOperation(string Name, string? Subject, DateTimeOffset StartedAt);
    private sealed class Scope(UiDiagnosticsService owner, Guid id) : IDisposable
    {
        private UiDiagnosticsService? owner = owner;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Complete(id);
    }
}
