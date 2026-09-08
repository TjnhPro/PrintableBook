using System.Windows;
using System.Windows.Threading;

namespace PrintableBook.Desktop.Updates;

public sealed class WpfUpdateApplicationLifetime(UpdateShutdownState shutdownState) : IUpdateApplicationLifetime
{
    public void RequestGracefulShutdown()
    {
        shutdownState.MarkRequested();
        Application.Current.Dispatcher.BeginInvoke(Application.Current.Shutdown, DispatcherPriority.ApplicationIdle);
    }

    public void RequestForceExit()
    {
        shutdownState.MarkRequested();
        Application.Current.Dispatcher.BeginInvoke(() => Environment.Exit(0), DispatcherPriority.ApplicationIdle);
    }
}
