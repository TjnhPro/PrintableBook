using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Processes one book project using Core contracts; concrete disk, image, and PDF work stays in Infrastructure.
/// </summary>
public sealed class WorkspaceBookProcessingQueueBookProcessor(
    IBookSourceScanner sourceScanner,
    IBookWorkspaceFactory workspaceFactory,
    IBookWorkspaceStateStore stateStore,
    ICoverValidator coverValidator,
    IInteriorShuffleStore shuffleStore,
    IInteriorPagePipeline interiorPagePipeline,
    IOrderedBookAssembler bookAssembler,
    IPrintableBookPdfExporter pdfExporter,
    IBookOutputPublisher outputPublisher) : IBookProcessingQueueBookProcessor
{
    public async ValueTask<BookProcessingQueueBookResult> ProcessBookAsync(
        PrintableBookProcessingCommand command,
        Action<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workspace = await workspaceFactory.CreateAsync(command.BookId, command.BookDirectory, cancellationToken);
        var priorState = await stateStore.LoadAsync(workspace, cancellationToken);
        var state = (priorState ?? BookProcessingState.NotStarted(command.BookId)).Start(DateTimeOffset.UtcNow, CreateConfigurationFingerprint(command));
        await PersistStateAsync(state, "book.started", command.BookId.Value, cancellationToken);

        try
        {
            state = await BeginStepAsync(state, "scan", cancellationToken);
            var scan = await sourceScanner.ScanAsync(command.BookId, command.BookDirectory, cancellationToken);
            if (!scan.IsSuccess)
            {
                throw new BookProcessingFailureException("scan", scan.Failure!);
            }

            state = await CompleteStepAsync(state, "scan", cancellationToken);
            var source = scan.Source!;
            string? cover = null;
            if (command.Mode == BookProcessingMode.InteriorOnly)
            {
                await stateStore.AppendLogAsync(workspace, new BookProcessingLogEntry(DateTimeOffset.UtcNow, "cover-validation.skipped", "Interior-only processing does not require a cover."), cancellationToken);
            }
            else
            {
                cover = SelectCover(source, command.SelectedCover).Reference;
                state = await BeginStepAsync(state, "cover-validation", cancellationToken);
                var coverValidation = await coverValidator.ValidateAsync(
                    new CoverValidationRequest(new FileReference(cover), command.MinimumCoverSize), cancellationToken);
                if (!coverValidation.IsValid)
                {
                    throw new BookProcessingFailureException("cover-validation", coverValidation.Failure!);
                }

                state = await CompleteStepAsync(state, "cover-validation", cancellationToken);
            }
            var interiorSources = source.GetAssets(BookAssetKind.Interior)
                .Select((asset, index) =>
                {
                    var sourceFile = new FileReference(asset.Reference);
                    return new InteriorSource(sourceFile,
                        InteriorSourceKey.FromBookRoot(command.BookDirectory, sourceFile),
                        $"page-{index + 1:D4}");
                })
                .ToArray();
            var activeInteriorSources = interiorSources
                .Where(item => priorState?.IsInteriorActive(item.SourceKey) ?? true)
                .ToArray();
            if (activeInteriorSources.Length == 0)
            {
                throw new BookProcessingFailureException("scan", new ProcessingFailure(
                    "book.no_active_interior_pages",
                    "Activate at least one Interior page before processing."));
            }

            var interiorRequests = activeInteriorSources
                .Select(item => new InteriorPagePipelineRequest(
                    workspace,
                    item.Source,
                    item.PageId,
                    command.ArtworkDetectionThreshold,
                    command.PreparedArtworkSize,
                    command.WorkingPageSize,
                    command.FinalPageSize,
                    command.TargetInteriorDensity,
                    command.Frame,
                    priorState?.GetInteriorFrameMode(item.SourceKey) ?? FrameMode.Auto))
                .ToArray();
            state = await BeginStepAsync(state, "interior-pages", cancellationToken);
            await using var concurrencyController = BookPageConcurrencyController.Create(command.MaximumPageConcurrency);
            var pageResults = await new BoundedInteriorPageBatchProcessor(interiorPagePipeline)
                .ProcessAsync(
                    interiorRequests,
                    concurrencyController,
                    (completed, total) => progress?.Invoke(new BookProcessingProgress(command.BookId, BookProcessingStatus.Running, "interior-pages", completed, total)),
                    cancellationToken);
            state = await CompleteStepAsync(state, "interior-pages", cancellationToken);

            state = await BeginStepAsync(state, "shuffle", cancellationToken);
            var shuffleMap = await shuffleStore.LoadAsync(workspace, cancellationToken);
            if (HasCompatiblePageSet(shuffleMap, pageResults) && command.ShuffleSeed is null)
            {
                if (shuffleMap!.Seed is null)
                {
                    shuffleMap = shuffleMap with { Seed = Random.Shared.Next() };
                    await shuffleStore.SaveAsync(workspace, shuffleMap, cancellationToken);
                }
            }
            else if (!HasCompatiblePageSet(shuffleMap, pageResults) || command.ShuffleSeed != shuffleMap!.Seed)
            {
                var effectiveSeed = command.ShuffleSeed ?? shuffleMap?.Seed ?? Random.Shared.Next();
                shuffleMap = InteriorShuffleIndexGenerator.Generate(pageResults.Select(page => page.Source).ToArray(), effectiveSeed);
                await shuffleStore.SaveAsync(workspace, shuffleMap, cancellationToken);
            }

            state = await CompleteStepAsync(state, "shuffle", cancellationToken);
            state = await BeginStepAsync(state, "assembly", cancellationToken);
            var assembly = await bookAssembler.AssembleAsync(new OrderedBookAssemblyRequest(
                workspace,
                command.Mode == BookProcessingMode.InteriorOnly
                    ? []
                    : source.GetAssets(BookAssetKind.Intro).Select(asset => new FileReference(asset.Reference)).ToArray(),
                pageResults,
                shuffleMap!,
                command.FinalPageSize,
                command.BackgroundPage), cancellationToken);
            state = await CompleteStepAsync(state, "assembly", cancellationToken);

            if (command.Mode == BookProcessingMode.InteriorOnly)
            {
                state = await BeginStepAsync(state, "interior-pdf-export", cancellationToken);
                var interiorPdf = await pdfExporter.ExportInteriorAsync(new InteriorPdfExportRequest(
                    assembly.OrderedPages,
                    workspace.TemporaryOutputDirectory,
                    command.InteriorPdfPageSize), cancellationToken);
                state = await CompleteStepAsync(state, "interior-pdf-export", cancellationToken);
                state = await BeginStepAsync(state, "interior-publish", cancellationToken);
                var publishedInterior = await outputPublisher.PublishInteriorAsync(new InteriorOutputPublicationRequest(
                    command.BookId,
                    interiorPdf,
                    command.FinalOutputRoot,
                    assembly.OrderedPages.Count,
                    command.InteriorPdfPageSize), cancellationToken);
                state = state.CompleteStep("interior-publish", DateTimeOffset.UtcNow);
                await PersistStateAsync(state, "step.completed", "interior-publish", CancellationToken.None);
                state = state
                    .RecordPublishedArtifacts([publishedInterior.InteriorPdf.Value])
                    .Complete(DateTimeOffset.UtcNow);
                await PersistStateAsync(state, "book.completed", command.BookId.Value, CancellationToken.None);
                return BookProcessingQueueBookResult.CompletedInterior(command.BookId, publishedInterior);
            }

            state = await BeginStepAsync(state, "pdf-export", cancellationToken);
            var pdfOutput = await pdfExporter.ExportAsync(new PrintableBookPdfExportRequest(
                new FileReference(cover!),
                assembly.OrderedPages,
                workspace.TemporaryOutputDirectory,
                command.CoverPdfPageSize,
                command.InteriorPdfPageSize), cancellationToken);
            state = await CompleteStepAsync(state, "pdf-export", cancellationToken);
            state = await BeginStepAsync(state, "publish", cancellationToken);
            var published = await outputPublisher.PublishAsync(new BookOutputPublicationRequest(
                command.BookId,
                pdfOutput,
                command.FinalOutputRoot,
                new PrintableBookPdfValidation(
                    1,
                    assembly.OrderedPages.Count,
                    command.CoverPdfPageSize,
                    command.InteriorPdfPageSize)), cancellationToken);
            state = state
                .CompleteStep("publish", DateTimeOffset.UtcNow);
            await PersistStateAsync(state, "step.completed", "publish", CancellationToken.None);
            state = state
                .RecordPublishedArtifacts([published.CoverPdf.Value, published.InteriorPdf.Value])
                .Complete(DateTimeOffset.UtcNow);
            await PersistStateAsync(state, "book.completed", command.BookId.Value, CancellationToken.None);
            return BookProcessingQueueBookResult.Completed(command.BookId, published);
        }
        catch (OperationCanceledException)
        {
            state = state.Cancel(DateTimeOffset.UtcNow);
            await PersistStateAsync(state, "book.cancelled", command.BookId.Value, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Cancelled, null, null);
        }
        catch (BookProcessingFailureException failure)
        {
            state = state.Fail(failure.Step, failure.Failure, DateTimeOffset.UtcNow);
            await stateStore.SaveErrorAsync(workspace, failure.Failure, CancellationToken.None);
            await PersistStateAsync(state, "book.failed", failure.Failure.Message, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Failed, failure.Failure, null);
        }
        catch (InteriorPageProcessingException failure)
        {
            var processingFailure = new ProcessingFailure("interior.page_failed", failure.Message);
            state = state.Fail("interior-pages", processingFailure, DateTimeOffset.UtcNow);
            await stateStore.SaveErrorAsync(workspace, processingFailure, CancellationToken.None);
            await PersistStateAsync(state, "book.failed", processingFailure.Message, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Failed, processingFailure, null);
        }
        catch (Exception exception)
        {
            var processingFailure = new ProcessingFailure("book.processing_failed", exception.Message);
            state = state.Fail(state.CurrentStep ?? "processing", processingFailure, DateTimeOffset.UtcNow);
            await stateStore.SaveErrorAsync(workspace, processingFailure, CancellationToken.None);
            await PersistStateAsync(state, "book.failed", processingFailure.Message, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Failed, processingFailure, null);
        }

        async ValueTask<BookProcessingState> CompleteStepAsync(BookProcessingState currentState, string step, CancellationToken token)
        {
            var completed = currentState.CompleteStep(step, DateTimeOffset.UtcNow);
            await PersistStateAsync(completed, "step.completed", step, token);
            return completed;
        }

        async ValueTask<BookProcessingState> BeginStepAsync(BookProcessingState currentState, string step, CancellationToken token)
        {
            progress?.Invoke(new BookProcessingProgress(command.BookId, BookProcessingStatus.Running, step));
            var started = currentState.BeginStep(step, DateTimeOffset.UtcNow);
            await PersistStateAsync(started, "step.started", step, token);
            return started;
        }

        async ValueTask PersistStateAsync(BookProcessingState currentState, string eventName, string detail, CancellationToken token)
        {
            await stateStore.SaveAsync(workspace, currentState, token);
            await stateStore.AppendLogAsync(workspace, new BookProcessingLogEntry(DateTimeOffset.UtcNow, eventName, detail), token);
        }
    }

    private static bool HasCompatiblePageSet(InteriorShuffleMap? shuffleMap, IReadOnlyList<InteriorPageProcessingResult> pageResults) =>
        shuffleMap is not null &&
        shuffleMap.Entries.Select(entry => entry.Page.Value).OrderBy(page => page, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(pageResults.Select(page => page.Source.Value).OrderBy(page => page, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private static string CreateConfigurationFingerprint(PrintableBookProcessingCommand command) =>
        string.Join("|", command.PreparedArtworkSize.Width, command.PreparedArtworkSize.Height,
            command.WorkingPageSize.Width, command.WorkingPageSize.Height,
            command.FinalPageSize.Width, command.FinalPageSize.Height,
            command.TargetInteriorDensity.Horizontal, command.TargetInteriorDensity.Vertical,
            command.CoverPdfPageSize.WidthInches, command.CoverPdfPageSize.HeightInches,
            command.InteriorPdfPageSize.WidthInches, command.InteriorPdfPageSize.HeightInches,
            command.MaximumPageConcurrency, command.ArtworkDetectionThreshold.Value,
            command.Frame?.Value, command.Mode);

    private static BookAsset SelectCover(BookSource source, FileReference? selectedCover)
    {
        var covers = source.GetAssets(BookAssetKind.Cover);
        if (selectedCover is not null)
        {
            var selected = covers.FirstOrDefault(candidate => string.Equals(candidate.Reference, selectedCover.Value, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) return selected;
            throw new BookProcessingFailureException("scan", new ProcessingFailure("book.cover_selection_invalid", "The selected cover is no longer available."));
        }

        if (covers.Count == 1) return covers[0];
        throw new BookProcessingFailureException("scan", new ProcessingFailure("book.cover_selection_required", "Select one cover candidate before processing."));
    }

    private sealed record InteriorSource(FileReference Source, string SourceKey, string PageId);

    private sealed class BookProcessingFailureException(string step, ProcessingFailure failure) : Exception(failure.Message)
    {
        public string Step { get; } = step;

        public ProcessingFailure Failure { get; } = failure;
    }
}
