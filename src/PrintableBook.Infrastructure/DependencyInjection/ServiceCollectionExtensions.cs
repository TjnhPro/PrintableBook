using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Scanning;

namespace PrintableBook.Infrastructure.DependencyInjection;

/// <summary>
/// Composition point for concrete adapter registrations as they are implemented.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrintableBookInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IBookSourceScanner, BookSourceScanner>();
        return services;
    }
}
