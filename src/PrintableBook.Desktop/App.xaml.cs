using Microsoft.Extensions.DependencyInjection;
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
}
