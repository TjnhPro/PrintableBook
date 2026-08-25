namespace PrintableBook.Core.Application.BackgroundTasks;

public sealed class BackgroundTaskConflictException(
    BackgroundTaskKind requestedKind,
    BackgroundTaskKind activeKind)
    : InvalidOperationException(
        $"Background task '{requestedKind}' cannot start while '{activeKind}' is active.")
{
    public BackgroundTaskKind RequestedKind { get; } = requestedKind;

    public BackgroundTaskKind ActiveKind { get; } = activeKind;
}
