namespace PrintableBook.Updater;

public sealed record UpdaterCommand(
    int WaitPid,
    string AppRoot,
    string PayloadDirectory,
    string UpdatesRoot,
    Version CurrentVersion)
{
    public string BackupDirectory => Path.Combine(UpdatesRoot, "backup", CurrentVersion.ToString(3));
}
