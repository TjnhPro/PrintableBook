using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Production;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public sealed class ProcessingSessionWorker(
    IApplicationSnapshotProvider snapshotProvider,
    IPrintableBookApplication application,
    IFileSystem fileSystem,
    IImageInspector imageInspector,
    IBrandValidationService brandValidationService) : BackgroundTaskWorker<ProcessingSessionWorkerRequest, BookProcessingQueueResult>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.ProcessingSession;

    protected override async ValueTask<BookProcessingQueueResult> ExecuteTypedAsync(
        ProcessingSessionWorkerRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        context.Report("Preparing", subject: request.BookIds.FirstOrDefault());
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var validated = Validate(snapshot, request, context);
        var books = validated.Books;
        var brand = validated.Brand;
        var settings = snapshot.GlobalSettings;
        var queue = books.Select((book, index) => new ProcessQueueEntry(book.Id, index == 0 ? BookProcessingStatus.Running : BookProcessingStatus.NotStarted, index == 0 ? "Preparing" : "Waiting")).ToArray();
        var currentBook = books[0].Id;
        var currentStep = "Preparing";
        var pagesCompleted = 0;
        var pagesTotal = 0;
        var progressSync = new Lock();

        void Publish(bool active = true, bool cancelling = false)
        {
            ProcessSessionSnapshot view;
            lock (progressSync)
            {
                view = new ProcessSessionSnapshot(active, cancelling, brand.Name, currentBook, currentStep, queue, pagesCompleted, pagesTotal, settings.MaximumPageConcurrency, request.StartedAt);
            }
            context.SetView(view);
        }

        Publish();
        var brandState = await brandValidationService.CheckStateAsync(brand.Directory, settings, cancellationToken);
        if (brandState.Status != BrandValidationStatus.Validated)
        {
            Fail(request, context, "process_brand_not_validated", $"Brand '{brand.Name}' must be validated before processing.");
        }

        // Brand validation already performed the expensive frame and background checks.
        // Processing receives only the certified Brand-owned files; Book-owned custom Intro
        // sources remain validated below because they are outside the Brand contract.
        var frame = new FileReference(Path.Combine(brand.Directory.Value, "frame.png"));

        var summaries = snapshot.BookSummaries.ToDictionary(summary => summary.BookId.Value, StringComparer.Ordinal);
        var introTemplatePagesByBook = new Dictionary<string, IReadOnlyList<FileReference>>(StringComparer.Ordinal);
        var customIntroFromBookInteriorByBook = new Dictionary<string, bool>(StringComparer.Ordinal);
        var validatedIntroSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var book in books)
        {
            var summary = summaries[book.Id.Value];
            IReadOnlyList<FileReference> pages;
            if (summary.HasIntro)
            {
                var selectedKeys = summary.SelectedIntroInteriorSourceKeys ?? [];
                if (selectedKeys.Count == 0)
                {
                    Fail(request, context, "process_intro_selection_required", "Choose at least one Book interior image for the custom Intro.", book.Id);
                }

                var interiorByKey = (summary.InteriorSourcePages ?? [])
                    .ToDictionary(
                        page => page.SourceKey ?? InteriorSourceKey.FromBookRoot(book.Directory, new FileReference(page.SourceReference)),
                        page => new FileReference(page.SourceReference),
                        StringComparer.OrdinalIgnoreCase);
                var resolved = new List<FileReference>(selectedKeys.Count);
                foreach (var key in selectedKeys)
                {
                    if (!interiorByKey.TryGetValue(key, out var page))
                    {
                        Fail(request, context, "process_intro_selection_missing", "A selected custom Intro source is no longer available in Book interior.", book.Id);
                    }
                    resolved.Add(page!);
                }

                pages = resolved;
                customIntroFromBookInteriorByBook[book.Id.Value] = true;
            }
            else
            {
                var selection = IntroTemplateSelectionResolver.Resolve(brand.IntroTemplateAssets);
                if (!selection.IsSuccess)
                {
                    var code = selection.Failure!.Code switch
                    {
                        "intro.template_empty" => "process_intro_template_empty",
                        _ => "process_intro_template_invalid"
                    };
                    Fail(request, context, code, selection.Failure.Message, book.Id);
                }

                pages = selection.Assets.Select(asset => new FileReference(asset.SourceReference)).ToArray();
                customIntroFromBookInteriorByBook[book.Id.Value] = false;
            }
            if (!summary.HasIntro)
            {
                introTemplatePagesByBook[book.Id.Value] = pages;
                continue;
            }
            foreach (var page in pages)
            {
                if (!validatedIntroSources.Add(page.Value)) continue;
                if (!await fileSystem.FileExistsAsync(page, cancellationToken))
                {
                    Fail(request, context, "process_intro_template_invalid", "A selected IntroTemplate image is no longer readable.", book.Id);
                }

                try
                {
                    var size = await imageInspector.GetSizeAsync(page, cancellationToken);
                    if (size.Width != size.Height || size.Width is not 1024 and not 2048)
                    {
                        Fail(request, context, "process_intro_template_invalid", "IntroTemplate images must be 1024×1024 or 2048×2048 pixels.", book.Id);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (BackgroundTaskFailureException)
                {
                    throw;
                }
                catch (Exception)
                {
                    Fail(request, context, "process_intro_template_invalid", "A selected IntroTemplate image cannot be read.", book.Id);
                }
            }
            introTemplatePagesByBook[book.Id.Value] = pages;
        }
        FileReference? background = null;
        if (books.Any(book => summaries[book.Id.Value].HasBackground))
        {
            background = new FileReference(Path.Combine(brand.Directory.Value, "background.png"));
        }

        if (request.Mode == BookProcessingMode.ProductionInterior)
        {
            var productionBook = books[0];
            foreach (var kind in new[] { ProductionAssetKind.InteriorCover, ProductionAssetKind.BookOwner })
            {
                var source = ProductionWorkspacePaths.SourceFile(productionBook.Workspace, kind);
                if (!await fileSystem.FileExistsAsync(source, cancellationToken))
                {
                    Fail(
                        request,
                        context,
                        "production_interior_assets_missing",
                        "Upload both Interior Cover and Book Owner Production assets before building Final Interior.",
                        productionBook.Id);
                }
            }
        }

        var processingRequest = new BookProcessingQueueRequest(books.Select(book => new PrintableBookProcessingCommand(
            book.Id,
            book.Directory,
            new DirectoryReference(Path.Combine(book.Directory.Value, "Output")),
            new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide),
            new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide),
            new ImageSize(settings.WorkingPageWidth, settings.WorkingPageHeight),
            new ImageSize(settings.FinalPageWidth, settings.FinalPageHeight),
            new ImageDensity(settings.Dpi, settings.Dpi),
            GlobalSettings.DefaultCoverPdfPageSize,
            settings.FinalInteriorPdfPageSize,
            settings.MaximumPageConcurrency,
            new ArtworkDetectionThreshold(settings.ArtworkDetectionThreshold),
            frame,
            null,
            SelectedCover: string.IsNullOrWhiteSpace(summaries[book.Id.Value].SelectedCoverReference) ? null : new FileReference(summaries[book.Id.Value].SelectedCoverReference!),
            Mode: request.Mode,
            BackgroundPage: summaries[book.Id.Value].HasBackground ? background : null,
            ArtworkSourceNormalization: settings.EffectiveArtworkSourceNormalization,
            BorderLineDetection: settings.EffectiveBorderLineDetection,
            IntroTemplatePages: introTemplatePagesByBook[book.Id.Value],
            CustomIntroFromBookInterior: customIntroFromBookInteriorByBook[book.Id.Value],
            ProductionPrefixSources: request.Mode == BookProcessingMode.ProductionInterior
                ?
                [
                    CreateProductionPrefix(book.Workspace, ProductionAssetKind.InteriorCover),
                    CreateProductionPrefix(book.Workspace, ProductionAssetKind.BookOwner)
                ]
                : null)).ToArray());

        void Report(BookProcessingProgress progress)
        {
            lock (progressSync)
            {
                if (currentBook != progress.BookId)
                {
                    currentBook = progress.BookId;
                    pagesCompleted = 0;
                    pagesTotal = 0;
                }
                currentStep = progress.Step;
                if (progress.PagesCompleted is not null) pagesCompleted = progress.PagesCompleted.Value;
                if (progress.PagesTotal is not null) pagesTotal = progress.PagesTotal.Value;
                var index = Array.FindIndex(queue, entry => entry.BookId == progress.BookId);
                if (index >= 0) queue[index] = queue[index] with { Status = progress.Status, Detail = progress.Detail ?? progress.Step };
            }
            Publish();
        }

        var result = await application.ProcessBooksAsync(processingRequest, Report, cancellationToken);
        lock (progressSync)
        {
            queue = result.Books.Select(book => new ProcessQueueEntry(book.BookId, book.Status, book.Failure?.Message)).ToArray();
            currentBook = null;
            currentStep = queue.Any(entry => entry.Status == BookProcessingStatus.Failed)
                ? "Failed"
                : queue.Any(entry => entry.Status == BookProcessingStatus.Cancelled) ? "Cancelled" : "Completed";
        }
        Publish(active: false);
        if (cancellationToken.IsCancellationRequested && result.Books.Any(book => book.Status == BookProcessingStatus.Cancelled))
        {
            throw new OperationCanceledException(cancellationToken);
        }
        return result;
    }

    private static ValidatedProcessingContext Validate(ApplicationSnapshot snapshot, ProcessingSessionWorkerRequest request, IBackgroundTaskContext context)
    {
        if (!Enum.IsDefined(request.Mode))
        {
            Fail(request, context, "process_mode_invalid", "The requested processing mode is not available.");
        }
        if (request.Mode == BookProcessingMode.ProductionInterior && request.BookIds.Count != 1)
        {
            Fail(request, context, "production_single_book_required", "Build Final Interior requires exactly one Book.");
        }
        var ids = request.BookIds.Distinct(StringComparer.Ordinal).ToArray();
        var resolution = BookBrandExecutionResolver.ResolveBatch(snapshot, ids);
        if (!resolution.IsSuccess)
        {
            var failure = resolution.Failure!;
            var code = failure.Code == "book_not_found" ? "process_book_not_found" : failure.Code;
            Fail(request, context, code, failure.Message, failure.BookId);
        }

        var selected = resolution.Books.Select(item => item.Book).ToArray();
        var summaries = resolution.Books.ToDictionary(item => item.Book.Id.Value, item => item.Summary, StringComparer.Ordinal);
        var notReady = selected.FirstOrDefault(book => !summaries.TryGetValue(book.Id.Value, out var summary) || !string.Equals(summary.ValidationStatus, "Ready", StringComparison.Ordinal));
        if (notReady is not null)
        {
            Fail(request, context, "process_book_not_ready", $"Book '{notReady.Id.Value}' is not ready for processing.", notReady.Id);
        }
        return new ValidatedProcessingContext(selected, resolution.Brand!);

    }

    private sealed record ValidatedProcessingContext(
        IReadOnlyList<DiscoveredBook> Books,
        DiscoveredBrand Brand);

    private static ProductionPrefixSource CreateProductionPrefix(BookWorkspace workspace, ProductionAssetKind kind)
    {
        var definition = ProductionAssets.Get(kind);
        return new ProductionPrefixSource(kind, ProductionWorkspacePaths.SourceFile(workspace, kind), definition.StablePageId);
    }

    private static void Fail(ProcessingSessionWorkerRequest request, IBackgroundTaskContext context, string code, string message, BookId? bookId = null)
    {
        var queue = request.BookIds.Select(id => new ProcessQueueEntry(new BookId(id), string.Equals(id, bookId?.Value, StringComparison.Ordinal) ? BookProcessingStatus.Failed : BookProcessingStatus.NotStarted, string.Equals(id, bookId?.Value, StringComparison.Ordinal) ? message : "Waiting")).ToArray();
        context.SetView(new ProcessSessionSnapshot(false, false, null, bookId, "Failed", queue, StartedAt: request.StartedAt));
        throw new BackgroundTaskFailureException(code, message);
    }
}
