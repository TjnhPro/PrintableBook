using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.DependencyInjection;
using PrintableBook.Infrastructure.DependencyInjection;
using System.Windows;

namespace PrintableBook.Desktop;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddPrintableBookCore();
        services.AddPrintableBookInfrastructure();
        services.AddSingleton<IProcessShutdownPrompt, ProcessShutdownPrompt>();
        services.AddSingleton<ProcessWindowShutdownCoordinator>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        try
        {
            // Windows may terminate this process immediately after this event, so this is a
            // deliberately bounded, best-effort wait. Do not display UI or cancel the OS action.
            serviceProvider?
                .GetService<IProcessSessionService>()?
                .StopAndWaitAsync(TimeSpan.FromSeconds(5))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // There is no reliable recovery path during system shutdown.
        }
        finally
        {
            base.OnSessionEnding(e);
        }
    }
}
