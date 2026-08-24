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
            var interiorRequests = source.GetAssets(BookAssetKind.Interior)
                .Select((asset, index) => new InteriorPagePipelineRequest(
                    workspace,
                    new FileReference(asset.Reference),
                    $"page-{index + 1:D4}",
                    command.ArtworkDetectionThreshold,
                    command.TargetInteriorSize,
                    command.TargetInteriorDensity,
                    command.Frame,
                    command.IsFrameEnabled))
                .ToArray();
            state = await BeginStepAsync(state, "interior-pages", cancellationToken);
            await using var concurrencyController = BookPageConcurrencyController.Create(command.MaximumPageConcurrency);
            var pageResults = await new BoundedInteriorPageBatchProcessor(interiorPagePipeline)
                .ProcessAsync(interiorRequests, concurrencyController, cancellationToken);
            state = await CompleteStepAsync(state, "interior-pages", cancellationToken);

            state = await BeginStepAsync(state, "shuffle", cancellationToken);
            var shuffleMap = await shuffleStore.LoadAsync(workspace, cancellationToken);
            if (!IsCompatible(shuffleMap, pageResults, command.ShuffleSeed))
            {
                shuffleMap = InteriorShuffleIndexGenerator.Generate(pageResults.Select(page => page.Source).ToArray(), command.ShuffleSeed);
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
                command.TargetInteriorSize), cancellationToken);
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

    private static bool IsCompatible(InteriorShuffleMap? shuffleMap, IReadOnlyList<InteriorPageProcessingResult> pageResults, int? seed) =>
        shuffleMap is not null &&
        shuffleMap.Seed == seed &&
        shuffleMap.Entries.Select(entry => entry.Page).OrderBy(page => page.Value)
            .SequenceEqual(pageResults.Select(page => page.Source).OrderBy(page => page.Value));

    private static string CreateConfigurationFingerprint(PrintableBookProcessingCommand command) =>
        string.Join("|", command.TargetInteriorSize.Width, command.TargetInteriorSize.Height,
            command.TargetInteriorDensity.Horizontal, command.TargetInteriorDensity.Vertical,
            command.CoverPdfPageSize.WidthInches, command.CoverPdfPageSize.HeightInches,
            command.InteriorPdfPageSize.WidthInches, command.InteriorPdfPageSize.HeightInches,
            command.MaximumPageConcurrency, command.ArtworkDetectionThreshold.Value,
            command.Frame?.Value, command.IsFrameEnabled, command.Mode);

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

    private sealed class BookProcessingFailureException(string step, ProcessingFailure failure) : Exception(failure.Message)
    {
        public string Step { get; } = step;

        public ProcessingFailure Failure { get; } = failure;
    }
}
