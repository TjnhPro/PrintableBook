using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Services;

namespace PrintableBook.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrintableBookCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IProcessingSessionGate, ProcessingSessionGate>();
        services.AddSingleton<IBookProcessingPipeline, BookProcessingPipeline>();
        services.AddSingleton<IPrintableBookApplication, PrintableBookApplication>();
        return services;
    }
}
