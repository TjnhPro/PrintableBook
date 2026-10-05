using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrintableBookCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IOperationDiagnostics, NoOpOperationDiagnostics>();
        services.AddSingleton<IProcessingSessionGate, ProcessingSessionGate>();
        services.AddSingleton<IArtworkClassifier, ArtworkClassifier>();
        services.AddSingleton<IApplicationSnapshotService, ApplicationSnapshotService>();
        services.AddSingleton<BrandValidationTargetResolver>();
        services.AddSingleton<BrandFingerprintCalculator>();
        services.AddSingleton<IBrandValidationService, BrandValidationService>();
        services.AddSingleton<IBrandTemplateCopyService, BrandTemplateCopyService>();
        services.AddSingleton<IProductionPageProcessingService, ProductionPageProcessingService>();
        services.AddSingleton<IProductionCoverPdfService, ProductionCoverPdfService>();
        services.AddSingleton<IBookCoverSelectionService, BookCoverSelectionService>();
        services.AddSingleton<IInteriorFrameModeService, InteriorFrameModeService>();
        services.AddSingleton<IBookInteriorSettingsService, BookInteriorSettingsService>();
        services.AddSingleton<IInteriorShuffleSeedSource, SecureInteriorShuffleSeedSource>();
        services.AddSingleton<IInteriorShuffleService, InteriorShuffleService>();
        services.AddSingleton<IBookCatalogMetadataService, BookCatalogMetadataService>();
        services.AddSingleton<IKeywordReceiptKeyProvider, ProcessKeywordReceiptKeyProvider>();
        services.AddSingleton<IKeywordPreviewReceiptProtector, KeywordPreviewReceiptProtector>();
        services.AddSingleton<IKeywordSeedSource, SecureKeywordSeedSource>();
        services.AddSingleton<IBuildIdFactory, GuidBuildIdFactory>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBookKeywordPreviewService, BookKeywordPreviewService>();
        services.AddSingleton<IInterruptedProcessingRecoveryService, InterruptedProcessingRecoveryService>();
        services.AddKeyedSingleton<IBackgroundTaskWorker, LibraryRefreshWorker>(BackgroundTaskKind.LibraryRefresh);
        services.AddKeyedSingleton<IBackgroundTaskWorker, ProcessingSessionWorker>(BackgroundTaskKind.ProcessingSession);
        services.AddKeyedSingleton<IBackgroundTaskWorker, CacheCleanupWorker>(BackgroundTaskKind.CacheCleanup);
        services.AddKeyedSingleton<IBackgroundTaskWorker, ProductionActionWorker>(BackgroundTaskKind.ProductionAction);
        services.AddKeyedSingleton<IBackgroundTaskWorker, AmazonAsinCrawlWorker>(BackgroundTaskKind.AmazonAsinCrawl);
        services.AddKeyedSingleton<IBackgroundTaskWorker, S3StorageWorker>(BackgroundTaskKind.S3Storage);
        services.AddSingleton<IAmazonCrawlDelay, AmazonCrawlJitterDelay>();
        services.AddSingleton<IProcessSessionService, ProcessSessionService>();
        services.AddSingleton<IAmazonAsinCrawlSessionService, AmazonAsinCrawlSessionService>();
        services.AddSingleton<IS3StorageOperationContextStore, S3StorageOperationContextStore>();
        services.AddSingleton<IBookOutputLeaseCoordinator, BookOutputLeaseCoordinator>();
        services.AddSingleton<IS3StorageService, S3StorageService>();
        services.AddSingleton<IBookProcessingPipeline, BookProcessingPipeline>();
        services.AddSingleton<IBookProcessingQueueBookProcessor, WorkspaceBookProcessingQueueBookProcessor>();
        services.AddSingleton<BookProcessingQueueProcessor>();
        services.AddSingleton<IPrintableBookApplication, PrintableBookApplication>();
        services.AddSingleton<IUpdateService, UpdateService>();
        return services;
    }
}
