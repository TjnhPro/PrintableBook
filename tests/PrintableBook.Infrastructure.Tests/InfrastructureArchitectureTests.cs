using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.DependencyInjection;
using PrintableBook.Infrastructure.DependencyInjection;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class InfrastructureArchitectureTests
{
    [Fact]
    public void InfrastructureDoesNotReferenceTheDesktopHost()
    {
        var referencedAssemblies = typeof(PrintableBook.Infrastructure.DependencyInjection.ServiceCollectionExtensions)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name);

        Assert.DoesNotContain(referencedAssemblies, name =>
            string.Equals(name, "PrintableBook.Desktop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Composition_resolves_the_cache_cleanup_worker_and_storage_maintenance()
    {
        var services = new ServiceCollection()
            .AddPrintableBookCore()
            .AddPrintableBookInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CacheCleanupWorker>(provider.GetRequiredKeyedService<IBackgroundTaskWorker>(BackgroundTaskKind.CacheCleanup));
        Assert.IsType<PhysicalBookStorageMaintenance>(provider.GetRequiredService<IBookStorageMaintenance>());
    }
}
