namespace PrintableBook.Desktop.Updates;

public enum DesktopUpdatePhase
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Verifying,
    Ready,
    Installing,
    Error
}
