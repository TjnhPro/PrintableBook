using Microsoft.Extensions.DependencyInjection;

namespace PrintableBook.Infrastructure.DependencyInjection;

/// <summary>
/// Composition point for concrete adapter registrations as they are implemented.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrintableBookInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
