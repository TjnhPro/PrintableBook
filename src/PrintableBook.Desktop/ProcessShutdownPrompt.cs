using System.Windows;

namespace PrintableBook.Desktop;

internal enum ActiveProcessCloseDecision
{
    StopAndExit,
    ContinueUsingApp
}

internal enum ProcessStopTimeoutDecision
{
    ForceExit,
    KeepWaiting
}

internal interface IProcessShutdownPrompt
{
    ActiveProcessCloseDecision ConfirmActiveProcessClose();
    ProcessStopTimeoutDecision ConfirmStopTimeout();
}

internal sealed class ProcessShutdownPrompt : IProcessShutdownPrompt
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
