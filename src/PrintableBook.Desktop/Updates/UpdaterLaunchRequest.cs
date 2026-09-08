namespace PrintableBook.Desktop.Updates;

public sealed record UpdaterLaunchRequest(
    int WaitPid,
    string AppRoot,
    string PayloadDirectory,
    string UpdatesRoot,
    Version CurrentVersion);
