using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Infrastructure.BrandValidation;
using PrintableBook.Infrastructure.Brands;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Discovery;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Pdf;
using PrintableBook.Infrastructure.Scanning;
using PrintableBook.Infrastructure.Workspaces;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.Updates;
using PrintableBook.Core.Application.Production;
using PrintableBook.Infrastructure.Production;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Infrastructure.AmazonCrawl;
using PrintableBook.Infrastructure.CloakBrowser;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Infrastructure.S3Storage;

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
        services.AddSingleton<IBrandValidationStateStore, JsonBrandValidationStateStore>();
        services.AddSingleton<IBrandMetadataStore, JsonBrandMetadataStore>();
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
        services.AddSingleton<IPrintableBookPdfExporter, PdfSharpPrintableBookPdfExporter>();
        services.AddSingleton<IPdfDocumentInspector, PdfSharpDocumentInspector>();
        services.AddSingleton<IBookOutputPublisher, ValidatedBookOutputPublisher>();
        services.AddSingleton<IBookSourceScanner, BookSourceScanner>();
        services.AddSingleton<IBookWorkspaceFactory, PhysicalBookWorkspaceFactory>();
        services.AddSingleton<IBookWorkspaceStateStore, JsonBookWorkspaceStateStore>();
        services.AddSingleton<IProductionWorkspaceStateStore, JsonProductionWorkspaceStateStore>();
        services.AddSingleton<IProductionAssetImportService, ProductionAssetImportService>();
        services.AddSingleton<IProductionPdfNameSuggestionService, FileSystemProductionPdfNameSuggestionService>();
        services.AddSingleton<IAmazonSearchHtmlParser, AmazonSearchHtmlParser>();
        services.AddSingleton<CloakBrowserAmazonPageClient>();
        services.AddSingleton<IAmazonSearchPageClient>(provider => provider.GetRequiredService<CloakBrowserAmazonPageClient>());
        services.AddSingleton<IAmazonBrowserLifetime>(provider => provider.GetRequiredService<CloakBrowserAmazonPageClient>());
        services.AddSingleton<IBookStorageMaintenance, PhysicalBookStorageMaintenance>();
        services.AddSingleton<IInteriorShuffleStore, JsonInteriorShuffleStore>();
        services.AddSingleton<IS3StorageSettingsStore, JsonS3StorageSettingsStore>();
        services.AddSingleton<IBookPublicationPackageStore, JsonBookPublicationPackageStore>();
        services.AddSingleton<IS3PublicationReceiptStore, JsonS3PublicationReceiptStore>();
        services.AddSingleton<IS3ObjectSessionFactory, AwsS3ObjectSessionFactory>();
        services.AddHttpClient("S3PublicVerification", client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(GitHubReleaseUpdateFeed.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IUpdateManifestPublicKeyProvider, ProductionUpdateManifestPublicKeyProvider>();
        services.AddSingleton<SignedReleaseManifestClient>();
        services.AddSingleton<IUpdateFeed, GitHubReleaseUpdateFeed>();
        services.AddHttpClient(HttpUpdateAssetDownloader.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<IUpdateStorageRootProvider, LocalAppDataUpdateStorageRootProvider>();
        services.AddSingleton<UpdateStorageLayout>();
        services.AddSingleton<HttpUpdateAssetDownloader>();
        services.AddSingleton<Sha256PackageVerifier>();
        services.AddSingleton<ZipUpdatePackageExtractor>();
        services.AddSingleton<UpdatePackageContractValidator>();
        services.AddSingleton<IUpdatePreparationService, UpdatePreparationService>();
        return services;
    }
}
