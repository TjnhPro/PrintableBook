using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;

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
        services.AddSingleton<IBookCoverSelectionService, BookCoverSelectionService>();
        services.AddSingleton<IInteriorFrameModeService, InteriorFrameModeService>();
        services.AddSingleton<IInterruptedProcessingRecoveryService, InterruptedProcessingRecoveryService>();
        services.AddKeyedSingleton<IBackgroundTaskWorker, LibraryRefreshWorker>(BackgroundTaskKind.LibraryRefresh);
        services.AddKeyedSingleton<IBackgroundTaskWorker, AssetPreviewWorker>(BackgroundTaskKind.AssetPreview);
        services.AddKeyedSingleton<IBackgroundTaskWorker, ProcessingSessionWorker>(BackgroundTaskKind.ProcessingSession);
        services.AddSingleton<IProcessSessionService, ProcessSessionService>();
        services.AddSingleton<IBookProcessingPipeline, BookProcessingPipeline>();
        services.AddSingleton<IBookProcessingQueueBookProcessor, WorkspaceBookProcessingQueueBookProcessor>();
        services.AddSingleton<BookProcessingQueueProcessor>();
        services.AddSingleton<IPrintableBookApplication, PrintableBookApplication>();
        return services;
    }
}
