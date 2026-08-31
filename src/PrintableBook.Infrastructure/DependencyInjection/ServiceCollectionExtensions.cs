using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Infrastructure.BrandValidation;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Discovery;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Pdf;
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
        services.AddSingleton<IApplicationRootDiscovery, PhysicalApplicationRootDiscovery>();
        services.AddSingleton<IBrandFrameResolver, PhysicalBrandFrameResolver>();
        services.AddSingleton<IGlobalSettingsStore, JsonGlobalSettingsStore>();
        services.AddSingleton<IBrandSettingsStore, JsonBrandSettingsStore>();
        services.AddSingleton<IBrandValidationStateStore, JsonBrandValidationStateStore>();
        services.AddSingleton<IImageInspector, MagickImageInspector>();
        services.AddSingleton<IArtworkSourceNormalizer, MagickArtworkSourceNormalizer>();
        services.AddSingleton<IBorderLineDetector, MagickBorderLineDetector>();
        services.AddSingleton<IBorderPixelDetector, MagickBorderPixelDetector>();
        services.AddSingleton<IBorderBoundsCropProcessor, MagickBorderBoundsCropProcessor>();
        services.AddSingleton<IArtworkTrimProcessor, MagickArtworkTrimProcessor>();
        services.AddSingleton<ISquareCropProcessor, MagickSquareCropProcessor>();
        services.AddSingleton<ISquareCanvasProcessor, MagickSquareCanvasProcessor>();
        services.AddSingleton<ISquarePadProcessor, MagickSquarePadProcessor>();
        services.AddSingleton<IArtworkResizeProcessor, MagickArtworkResizeProcessor>();
        services.AddSingleton<BorderArtPreparationProcessor>();
        services.AddSingleton<FullArtPreparationProcessor>();
        services.AddSingleton<CropArtPreparationProcessor>();
        services.AddSingleton<IArtworkPreparationService, ArtworkPreparationService>();
        services.AddSingleton<IFrameProcessor, MagickFrameProcessor>();
        services.AddSingleton<IWorkingPageProcessor, MagickWorkingPageProcessor>();
        services.AddSingleton<IFinalInteriorPageProcessor, MagickFinalInteriorPageProcessor>();
        services.AddSingleton<ICoverValidator, MagickCoverValidator>();
        services.AddSingleton<IInteriorPagePipeline, DiskBackedInteriorPagePipeline>();
        services.AddSingleton<IOrderedBookAssembler, OrderedBookAssembler>();
        services.AddSingleton<IPrintableBookPdfExporter, MagickPrintableBookPdfExporter>();
        services.AddSingleton<IPdfDocumentInspector, PdfSharpDocumentInspector>();
        services.AddSingleton<IBookOutputPublisher, ValidatedBookOutputPublisher>();
        services.AddSingleton<IBookSourceScanner, BookSourceScanner>();
        services.AddSingleton<IBookWorkspaceFactory, PhysicalBookWorkspaceFactory>();
        services.AddSingleton<IBookWorkspaceStateStore, JsonBookWorkspaceStateStore>();
        services.AddSingleton<IBookStorageMaintenance, PhysicalBookStorageMaintenance>();
        services.AddSingleton<IInteriorShuffleStore, JsonInteriorShuffleStore>();
        return services;
    }
}
