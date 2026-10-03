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
                ? "Processing or S3 publishing is currently running. Stopping S3 after uploads begin can leave a partial remote set until Upload is retried.\n\nYes: stop active work and restart to install the update.\nNo: keep the application open."
                : "Processing or S3 publishing is currently running. Stopping S3 after uploads begin can leave a partial remote set until Upload is retried.\n\nYes: stop active work and exit.\nNo: keep the application open.",
            "Work in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes
            ? ActiveProcessCloseDecision.StopAndExit
            : ActiveProcessCloseDecision.ContinueUsingApp;

    public ProcessStopTimeoutDecision ConfirmStopTimeout(ProcessShutdownIntent intent) =>
        MessageBox.Show(
            intent == ProcessShutdownIntent.RestartForUpdate
                ? "Active work did not stop within 5 seconds. Forcing restart may interrupt S3 publication.\n\nYes: force restart now and continue the update.\nNo: keep waiting for a clean shutdown."
                : "Active work did not stop within 5 seconds. Forcing exit may interrupt S3 publication.\n\nYes: force exit now.\nNo: keep waiting for a clean shutdown.",
            "Work is still stopping",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes
            ? ProcessStopTimeoutDecision.ForceExit
            : ProcessStopTimeoutDecision.KeepWaiting;
}
