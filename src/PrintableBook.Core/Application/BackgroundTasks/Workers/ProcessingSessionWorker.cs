using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public sealed class ProcessingSessionWorker(
    IApplicationSnapshotProvider snapshotProvider,
    IPrintableBookApplication application,
    IBrandFrameResolver brandFrameResolver,
    IFileSystem fileSystem,
    IImageInspector imageInspector) : BackgroundTaskWorker<ProcessingSessionWorkerRequest, BookProcessingQueueResult>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.ProcessingSession;

    protected override async ValueTask<BookProcessingQueueResult> ExecuteTypedAsync(
        ProcessingSessionWorkerRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        context.Report("Preparing", subject: request.BookIds.FirstOrDefault());
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var books = Validate(snapshot, request, context);
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
                view = new ProcessSessionSnapshot(active, cancelling, request.BrandName, currentBook, currentStep, queue, pagesCompleted, pagesTotal, settings.MaximumPageConcurrency, request.StartedAt);
            }
            context.SetView(view);
        }

        Publish();
        var brand = snapshot.Discovery.Brands.First(item => string.Equals(item.Name, request.BrandName, StringComparison.Ordinal));
        var frame = await brandFrameResolver.ResolveCompatibleFrameAsync(
            brand,
            new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide),
            cancellationToken);

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
            var candidate = new FileReference(Path.Combine(brand.Directory.Value, "background.png"));
            if (!await fileSystem.FileExistsAsync(candidate, cancellationToken))
            {
                Fail(request, context, "process_background_missing", "The selected Brand does not contain background.png.");
            }

            try
            {
                var size = await imageInspector.GetSizeAsync(candidate, cancellationToken);
                var expected = new ImageSize(settings.FinalPageWidth, settings.FinalPageHeight);
                if (size != expected)
                {
                    Fail(request, context, "process_background_invalid", $"Brand background.png must be {expected.Width}×{expected.Height} pixels.");
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
                Fail(request, context, "process_background_invalid", "The selected Brand background.png cannot be read.");
            }

            background = candidate;
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
            new PhysicalPageSize(settings.InteriorPdfWidthInches, settings.InteriorPdfHeightInches),
            new PhysicalPageSize(settings.InteriorPdfWidthInches, settings.InteriorPdfHeightInches),
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
            CustomIntroFromBookInterior: customIntroFromBookInteriorByBook[book.Id.Value])).ToArray());

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

    private static IReadOnlyList<DiscoveredBook> Validate(ApplicationSnapshot snapshot, ProcessingSessionWorkerRequest request, IBackgroundTaskContext context)
    {
        if (!snapshot.Discovery.Brands.Any(brand => string.Equals(brand.Name, request.BrandName, StringComparison.Ordinal)))
        {
            Fail(request, context, "process_brand_not_found", "The selected Brand no longer exists.");
        }
        var ids = request.BookIds.Distinct(StringComparer.Ordinal).ToArray();
        var selected = snapshot.Discovery.Books.Where(book => ids.Contains(book.Id.Value, StringComparer.Ordinal)).ToArray();
        if (selected.Length != ids.Length)
        {
            Fail(request, context, "process_book_not_found", "One or more selected Books no longer exist.");
        }
        var summaries = snapshot.BookSummaries.ToDictionary(summary => summary.BookId.Value, StringComparer.Ordinal);
        var notReady = selected.FirstOrDefault(book => !summaries.TryGetValue(book.Id.Value, out var summary) || !string.Equals(summary.ValidationStatus, "Ready", StringComparison.Ordinal));
        if (notReady is not null)
        {
            Fail(request, context, "process_book_not_ready", $"Book '{notReady.Id.Value}' is not ready for processing.", notReady.Id);
        }
        return selected;

    }

    private static void Fail(ProcessingSessionWorkerRequest request, IBackgroundTaskContext context, string code, string message, BookId? bookId = null)
    {
        var queue = request.BookIds.Select(id => new ProcessQueueEntry(new BookId(id), string.Equals(id, bookId?.Value, StringComparison.Ordinal) ? BookProcessingStatus.Failed : BookProcessingStatus.NotStarted, string.Equals(id, bookId?.Value, StringComparison.Ordinal) ? message : "Waiting")).ToArray();
        context.SetView(new ProcessSessionSnapshot(false, false, request.BrandName, bookId, "Failed", queue, StartedAt: request.StartedAt));
        throw new BackgroundTaskFailureException(code, message);
    }
}
