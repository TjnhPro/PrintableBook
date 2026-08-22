using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Scanning;
using PrintableBook.Infrastructure.Workspaces;

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
        services.AddSingleton<IImageInspector, MagickImageInspector>();
        services.AddSingleton<IArtworkTrimProcessor, MagickArtworkTrimProcessor>();
        services.AddSingleton<ISquareCanvasProcessor, MagickSquareCanvasProcessor>();
        services.AddSingleton<IArtworkResizeProcessor, MagickArtworkResizeProcessor>();
        services.AddSingleton<IFrameProcessor, MagickFrameProcessor>();
        services.AddSingleton<IFinalInteriorPageProcessor, MagickFinalInteriorPageProcessor>();
        services.AddSingleton<IBookSourceScanner, BookSourceScanner>();
        services.AddSingleton<IBookWorkspaceFactory, PhysicalBookWorkspaceFactory>();
        services.AddSingleton<IBookWorkspaceStateStore, JsonBookWorkspaceStateStore>();
        return services;
    }
}
