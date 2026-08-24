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

public interface IProcessShutdownPrompt
{
    ActiveProcessCloseDecision ConfirmActiveProcessClose();
    ProcessStopTimeoutDecision ConfirmStopTimeout();
}

public sealed class ProcessShutdownPrompt : IProcessShutdownPrompt
{
    public ActiveProcessCloseDecision ConfirmActiveProcessClose() =>
        MessageBox.Show(
            "Processing is currently running.\n\nYes: stop processing and exit.\nNo: keep the application open.",
            "Processing in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes
            ? ActiveProcessCloseDecision.StopAndExit
            : ActiveProcessCloseDecision.ContinueUsingApp;

    public ProcessStopTimeoutDecision ConfirmStopTimeout() =>
        MessageBox.Show(
            "Processing did not stop within 5 seconds.\n\nYes: force exit now.\nNo: keep waiting for a clean shutdown.",
            "Processing is still stopping",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes
            ? ProcessStopTimeoutDecision.ForceExit
            : ProcessStopTimeoutDecision.KeepWaiting;
}
