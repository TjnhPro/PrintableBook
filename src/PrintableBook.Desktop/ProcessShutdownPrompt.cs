using System.Windows;

namespace PrintableBook.Desktop;

public enum ActiveProcessCloseDecision
{
    StopAndExit,
    ContinueUsingApp
}

public enum ProcessStopTimeoutDecision
{
    ForceExit,
    KeepWaiting
}

public enum ProcessShutdownIntent
{
    ExitApplication,
    RestartForUpdate
}

public interface IProcessShutdownPrompt
{
    ActiveProcessCloseDecision ConfirmActiveProcessClose(ProcessShutdownIntent intent);
    ProcessStopTimeoutDecision ConfirmStopTimeout(ProcessShutdownIntent intent);
}

public sealed class ProcessShutdownPrompt : IProcessShutdownPrompt
{
    public ActiveProcessCloseDecision ConfirmActiveProcessClose(ProcessShutdownIntent intent) =>
        MessageBox.Show(
            intent == ProcessShutdownIntent.RestartForUpdate
                ? "Processing is currently running.\n\nYes: stop processing and restart to install the update.\nNo: keep the application open."
                : "Processing is currently running.\n\nYes: stop processing and exit.\nNo: keep the application open.",
            "Processing in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes
            ? ActiveProcessCloseDecision.StopAndExit
            : ActiveProcessCloseDecision.ContinueUsingApp;

    public ProcessStopTimeoutDecision ConfirmStopTimeout(ProcessShutdownIntent intent) =>
        MessageBox.Show(
            intent == ProcessShutdownIntent.RestartForUpdate
                ? "Processing did not stop within 5 seconds.\n\nYes: force restart now and continue the update.\nNo: keep waiting for a clean shutdown."
                : "Processing did not stop within 5 seconds.\n\nYes: force exit now.\nNo: keep waiting for a clean shutdown.",
            "Processing is still stopping",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes
            ? ProcessStopTimeoutDecision.ForceExit
            : ProcessStopTimeoutDecision.KeepWaiting;
}
