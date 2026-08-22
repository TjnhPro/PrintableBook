using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;

namespace PrintableBook.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrintableBookCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IProcessingSessionGate, ProcessingSessionGate>();
        services.AddSingleton<IApplicationSnapshotService, ApplicationSnapshotService>();
        services.AddSingleton<IProcessSessionService, ProcessSessionService>();
        services.AddSingleton<IBookProcessingPipeline, BookProcessingPipeline>();
        services.AddSingleton<IBookProcessingQueueBookProcessor, WorkspaceBookProcessingQueueBookProcessor>();
        services.AddSingleton<BookProcessingQueueProcessor>();
        services.AddSingleton<IPrintableBookApplication, PrintableBookApplication>();
        return services;
    }
}
