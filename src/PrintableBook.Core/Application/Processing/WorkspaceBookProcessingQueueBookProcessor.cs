using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Production;
using System.Security.Cryptography;
using System.Text;

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
    IBookOutputPublisher outputPublisher,
    IProductionWorkspaceStateStore? productionStateStore = null,
    IFileSystem? fileSystem = null) : IBookProcessingQueueBookProcessor
{
    public async ValueTask<BookProcessingQueueBookResult> ProcessBookAsync(
        PrintableBookProcessingCommand command,
        Action<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workspace = await workspaceFactory.CreateAsync(command.BookId, command.BookDirectory, cancellationToken);
        BookProcessingState? priorState;
        try
        {
            priorState = await stateStore.LoadAsync(workspace, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return new BookProcessingQueueBookResult(
                command.BookId,
                BookProcessingStatus.Failed,
                new ProcessingFailure(
                    "WORKSPACE_STATE_CORRUPT",
                    $"Book '{command.BookId.Value}' cannot be processed because its workspace state is invalid. The state file was left unchanged. Restore or repair it, then retry. {exception.Message}"),
                null);
        }
        var state = (priorState ?? BookProcessingState.NotStarted(command.BookId)).Start(DateTimeOffset.UtcNow, CreateConfigurationFingerprint(command));
        DirectoryReference? stagedFrameDirectory = null;
        var processedPreviewMayHaveChanged = false;
        await PersistStateAsync(state, "book.started", command.BookId.Value, cancellationToken);

        try
        {
            state = await BeginStepAsync(state, "scan", cancellationToken);
            var scan = await sourceScanner.ScanAsync(command.BookId, command.BookDirectory, cancellationToken);
            if (!scan.IsSuccess)
            {
                throw new BookProcessingFailureException("scan", scan.Failure!);
            }

            var validation = BookSourceValidator.Validate(scan.Source!);
            if (!validation.IsSuccess)
            {
                throw new BookProcessingFailureException("scan", validation.Failure!);
            }

            state = await CompleteStepAsync(state, "scan", cancellationToken);
            var source = validation.Source;
            string? cover = null;
            if (command.Mode is BookProcessingMode.InteriorOnly or BookProcessingMode.ProductionInterior)
            {
                await stateStore.AppendLogAsync(workspace, new BookProcessingLogEntry(DateTimeOffset.UtcNow, "cover-validation.skipped", "Interior PDF processing does not require a source cover."), cancellationToken);
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
            var customIntroKeys = command.CustomIntroFromBookInterior
                ? command.EffectiveIntroTemplatePages
                    .Select(page => InteriorSourceKey.FromBookRoot(command.BookDirectory, page))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            if (command.CustomIntroFromBookInterior && customIntroKeys.Count == 0)
            {
                throw new BookProcessingFailureException("scan", new ProcessingFailure("process_intro_selection_required", "Choose at least one Book interior image before processing a custom Intro selection."));
            }
            if (command.CustomIntroFromBookInterior &&
                (customIntroKeys.Count != command.EffectiveIntroTemplatePages.Count || customIntroKeys.Any(key => interiorSources.All(source => !string.Equals(source.SourceKey, key, StringComparison.OrdinalIgnoreCase)))))
            {
                throw new BookProcessingFailureException("scan", new ProcessingFailure("process_intro_selection_missing", "A selected custom Intro source is no longer available in Book interior."));
            }

            var normalInteriorSources = interiorSources
                .Where(item => !customIntroKeys.Contains(item.SourceKey))
                .ToArray();
            var activeInteriorSources = normalInteriorSources
                .Where(item => priorState?.IsInteriorActive(item.SourceKey) ?? true)
                .ToArray();
            if (activeInteriorSources.Length == 0)
            {
                throw new BookProcessingFailureException("scan", new ProcessingFailure(
                    "book.no_active_interior_pages",
                    "Activate at least one Interior page before processing."));
            }

            StagedFrameAsset? stagedFrame = null;
            var framePage = activeInteriorSources.FirstOrDefault(item =>
                (priorState?.GetInteriorFrameMode(item.SourceKey) ?? FrameMode.Disabled) == FrameMode.Enabled);
            if (framePage is not null)
            {
                if (command.Frame is null)
                {
                    throw new BookProcessingFailureException(
                        "interior-pages",
                        new ProcessingFailure(
                            "INTERIOR_FRAME_REQUIRED",
                            $"Book '{command.BookId.Value}' page '{framePage.PageId}' uses Frame, but its assigned Brand frame is missing. Validate the Brand frame and retry."));
                }

                stagedFrame = await StageFrameAsync(workspace, command.Frame, cancellationToken);
                stagedFrameDirectory = stagedFrame.Directory;
            }

            var introRequests = command.EffectiveIntroTemplatePages
                .Select((sourceFile, index) => new InteriorPagePipelineRequest(
                    workspace,
                    sourceFile,
                    $"intro-{index + 1:D4}",
                    command.ArtworkDetectionThreshold,
                    command.PreparedArtworkSize,
                    command.WorkingPageSize,
                    command.FinalPageSize,
                    command.TargetInteriorDensity,
                    null,
                    FrameMode.Disabled,
                    command.ArtworkSourceNormalization,
                    command.BorderLineDetection,
                    command.CustomIntroFromBookInterior
                        ? InteriorPageProcessingKind.IntroTemplate
                        : InteriorPageProcessingKind.BrandIntroTemplate))
                .ToArray();
            var interiorRequests = activeInteriorSources
                .Select(item =>
                {
                    var frameMode = priorState?.GetInteriorFrameMode(item.SourceKey) ?? FrameMode.Disabled;
                    return new InteriorPagePipelineRequest(
                        workspace,
                        item.Source,
                        item.PageId,
                        command.ArtworkDetectionThreshold,
                        command.PreparedArtworkSize,
                        command.WorkingPageSize,
                        command.FinalPageSize,
                        command.TargetInteriorDensity,
                        frameMode == FrameMode.Enabled ? stagedFrame?.File : null,
                        frameMode,
                        command.ArtworkSourceNormalization,
                        command.BorderLineDetection,
                        frameContentSha256: frameMode == FrameMode.Enabled ? stagedFrame?.Sha256 : null);
                })
                .ToArray();
            var productionPrefixSources = ValidateProductionPrefixSources(command);
            var productionRequests = productionPrefixSources
                .Select(item => new InteriorPagePipelineRequest(
                    workspace,
                    item.Source,
                    item.PageId,
                    command.ArtworkDetectionThreshold,
                    command.PreparedArtworkSize,
                    command.WorkingPageSize,
                    command.FinalPageSize,
                    command.TargetInteriorDensity,
                    null,
                    FrameMode.Disabled,
                    command.ArtworkSourceNormalization,
                    command.BorderLineDetection,
                    InteriorPageProcessingKind.ProductionInterior,
                    ProductionAssets.Get(item.AssetKind).ProcessedFileName))
                .ToArray();
            await using var concurrencyController = BookPageConcurrencyController.Create(command.MaximumPageConcurrency);
            var pageBatchProcessor = new BoundedInteriorPageBatchProcessor(interiorPagePipeline);
            IReadOnlyList<InteriorPageProcessingResult> productionResults = [];
            if (productionRequests.Length > 0)
            {
                state = await BeginStepAsync(state, "production-prefix-pages", cancellationToken);
                productionResults = await pageBatchProcessor.ProcessAsync(
                    productionRequests,
                    concurrencyController,
                    (completed, total) => progress?.Invoke(new BookProcessingProgress(command.BookId, BookProcessingStatus.Running, "production-prefix-pages", completed, total)),
                    cancellationToken);
                state = await CompleteStepAsync(state, "production-prefix-pages", cancellationToken);
            }
            IReadOnlyList<InteriorPageProcessingResult> introResults = [];
            if (introRequests.Length > 0)
            {
                processedPreviewMayHaveChanged = command.Mode == BookProcessingMode.InteriorOnly;
                state = await BeginStepAsync(state, "intro-pages", cancellationToken);
                introResults = await pageBatchProcessor.ProcessAsync(
                    introRequests,
                    concurrencyController,
                    (completed, total) => progress?.Invoke(new BookProcessingProgress(command.BookId, BookProcessingStatus.Running, "intro-pages", completed, total)),
                    cancellationToken);
                state = await CompleteStepAsync(state, "intro-pages", cancellationToken);
            }

            processedPreviewMayHaveChanged = command.Mode == BookProcessingMode.InteriorOnly;
            state = await BeginStepAsync(state, "interior-pages", cancellationToken);
            var pageResults = await pageBatchProcessor.ProcessAsync(
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
                introResults.Select(result => result.FinalPage).ToArray(),
                pageResults,
                shuffleMap!,
                command.FinalPageSize,
                command.BackgroundPage,
                productionResults.Select(result => result.FinalPage).ToArray()), cancellationToken);
            state = await CompleteStepAsync(state, "assembly", cancellationToken);

            if (command.Mode == BookProcessingMode.InteriorOnly)
            {
                var completedAt = DateTimeOffset.UtcNow;
                state = state
                    .RecordProcessedInteriorPreviews(pageResults.Select(page => new PublishedInteriorPreview(page.PageId, page.FinalPage.Value)))
                    .Complete(completedAt);
                await PersistStateAsync(state, "book.completed", command.BookId.Value, CancellationToken.None);
                return BookProcessingQueueBookResult.CompletedPreparation(command.BookId);
            }

            if (command.Mode == BookProcessingMode.ProductionInterior)
            {
                state = await BeginStepAsync(state, "interior-pdf-export", cancellationToken);
                var interiorPdf = await pdfExporter.ExportInteriorAsync(new InteriorPdfExportRequest(
                    assembly.IntroPages,
                    assembly.OrderedInteriorPages,
                    assembly.BackgroundPage,
                    workspace.TemporaryOutputDirectory,
                    command.InteriorPdfPageSize,
                    command.MaximumPageConcurrency,
                    assembly.ProductionPrefixPages), cancellationToken);
                state = await CompleteStepAsync(state, "interior-pdf-export", cancellationToken);
                state = await BeginStepAsync(state, "interior-publish", cancellationToken);
                var publishedInterior = await outputPublisher.PublishInteriorAsync(new InteriorOutputPublicationRequest(
                    command.BookId,
                    interiorPdf,
                    command.FinalOutputRoot,
                    assembly.OutputPageCount,
                    command.InteriorPdfPageSize), cancellationToken);
                state = state.CompleteStep("interior-publish", DateTimeOffset.UtcNow);
                await PersistStateAsync(state, "step.completed", "interior-publish", CancellationToken.None);
                var interiorPublishedAt = DateTimeOffset.UtcNow;
                state = state
                    .RecordPublishedInterior(
                        publishedInterior.InteriorPdf.Value,
                        InteriorOutputKind.Production,
                        interiorPublishedAt,
                        publishedInterior.PreviewPdf?.Value)
                    .RecordProcessedInteriorPreviews(pageResults.Select(page => new PublishedInteriorPreview(page.PageId, page.FinalPage.Value)))
                    .Complete(interiorPublishedAt);
                await RecordProductionInteriorStateAsync(
                    workspace,
                    command,
                    productionPrefixSources,
                    productionResults,
                    activeInteriorSources,
                    shuffleMap!,
                    publishedInterior.InteriorPdf,
                    publishedInterior.PreviewPdf,
                    interiorPublishedAt,
                    cancellationToken);
                await PersistStateAsync(state, "book.completed", command.BookId.Value, CancellationToken.None);
                return BookProcessingQueueBookResult.CompletedInterior(command.BookId, publishedInterior);
            }

            state = await BeginStepAsync(state, "pdf-export", cancellationToken);
            var pdfOutput = await pdfExporter.ExportAsync(new PrintableBookPdfExportRequest(
                new FileReference(cover!),
                assembly.IntroPages,
                assembly.OrderedInteriorPages,
                assembly.BackgroundPage,
                workspace.TemporaryOutputDirectory,
                command.CoverPdfPageSize,
                command.InteriorPdfPageSize,
                command.MaximumPageConcurrency,
                assembly.ProductionPrefixPages), cancellationToken);
            state = await CompleteStepAsync(state, "pdf-export", cancellationToken);
            state = await BeginStepAsync(state, "publish", cancellationToken);
            var published = await outputPublisher.PublishAsync(new BookOutputPublicationRequest(
                command.BookId,
                pdfOutput,
                command.FinalOutputRoot,
                new PrintableBookPdfValidation(
                    1,
                    assembly.OutputPageCount,
                    command.CoverPdfPageSize,
                    command.InteriorPdfPageSize)), cancellationToken);
            state = state
                .CompleteStep("publish", DateTimeOffset.UtcNow);
            await PersistStateAsync(state, "step.completed", "publish", CancellationToken.None);
            var publishedAt = DateTimeOffset.UtcNow;
            state = state
                .RecordPublishedArtifact(PublishedArtifactKind.Cover, published.CoverPdf.Value, published.CoverPreviewPdf?.Value)
                .RecordPublishedInterior(published.InteriorPdf.Value, InteriorOutputKind.Base, publishedAt, published.InteriorPreviewPdf?.Value)
                .RecordProcessedInteriorPreviews(pageResults.Select(page => new PublishedInteriorPreview(page.PageId, page.FinalPage.Value)))
                .Complete(publishedAt);
            await PersistStateAsync(state, "book.completed", command.BookId.Value, CancellationToken.None);
            return BookProcessingQueueBookResult.Completed(command.BookId, published);
        }
        catch (OperationCanceledException)
        {
            if (processedPreviewMayHaveChanged) state = state.ClearProcessedInteriorPreviews();
            state = state.Cancel(DateTimeOffset.UtcNow);
            await PersistStateAsync(state, "book.cancelled", command.BookId.Value, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Cancelled, null, null);
        }
        catch (BookProcessingFailureException failure)
        {
            if (processedPreviewMayHaveChanged) state = state.ClearProcessedInteriorPreviews();
            state = state.Fail(failure.Step, failure.Failure, DateTimeOffset.UtcNow);
            await stateStore.SaveErrorAsync(workspace, failure.Failure, CancellationToken.None);
            await PersistStateAsync(state, "book.failed", failure.Failure.Message, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Failed, failure.Failure, null);
        }
        catch (InteriorPageProcessingException failure)
        {
            var isIntro = failure.ProcessingKind is InteriorPageProcessingKind.IntroTemplate or InteriorPageProcessingKind.BrandIntroTemplate;
            var isProduction = failure.ProcessingKind == InteriorPageProcessingKind.ProductionInterior;
            var failureCode = failure.FailureCode ?? (isIntro ? "intro.page_failed" : isProduction ? "production.page_failed" : "interior.page_failed");
            var failureStep = isIntro ? "intro-pages" : isProduction ? "production-prefix-pages" : "interior-pages";
            var processingFailure = new ProcessingFailure(failureCode, failure.Message);
            if (processedPreviewMayHaveChanged) state = state.ClearProcessedInteriorPreviews();
            state = state.Fail(failureStep, processingFailure, DateTimeOffset.UtcNow);
            await stateStore.SaveErrorAsync(workspace, processingFailure, CancellationToken.None);
            await PersistStateAsync(state, "book.failed", processingFailure.Message, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Failed, processingFailure, null);
        }
        catch (Exception exception)
        {
            var processingFailure = new ProcessingFailure("book.processing_failed", exception.Message);
            if (processedPreviewMayHaveChanged) state = state.ClearProcessedInteriorPreviews();
            state = state.Fail(state.CurrentStep ?? "processing", processingFailure, DateTimeOffset.UtcNow);
            await stateStore.SaveErrorAsync(workspace, processingFailure, CancellationToken.None);
            await PersistStateAsync(state, "book.failed", processingFailure.Message, CancellationToken.None);
            return new BookProcessingQueueBookResult(command.BookId, BookProcessingStatus.Failed, processingFailure, null);
        }
        finally
        {
            if (stagedFrameDirectory is not null)
            {
                try
                {
                    Directory.Delete(stagedFrameDirectory.Value, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Best effort: cache cleanup can remove abandoned run inputs later.
                }
            }
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

    private static async ValueTask<StagedFrameAsset> StageFrameAsync(
        BookWorkspace workspace,
        FileReference source,
        CancellationToken cancellationToken)
    {
        var runDirectory = new DirectoryReference(Path.Combine(
            workspace.WorkingDirectory.Value,
            "cache",
            "_frame-input",
            Guid.NewGuid().ToString("N")));
        var staged = new FileReference(Path.Combine(runDirectory.Value, "frame.png"));
        try
        {
            Directory.CreateDirectory(runDirectory.Value);
            await using (var input = new FileStream(source.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(staged.Value, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            await using var stagedInput = new FileStream(staged.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(stagedInput, cancellationToken)).ToLowerInvariant();
            return new StagedFrameAsset(runDirectory, staged, digest);
        }
        catch (OperationCanceledException)
        {
            TryDeleteStagedFrameDirectory(runDirectory);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteStagedFrameDirectory(runDirectory);
            throw new BookProcessingFailureException(
                "interior-pages",
                new ProcessingFailure(
                    "INTERIOR_FRAME_INVALID",
                    $"The assigned Brand frame for Book '{workspace.BookId.Value}' could not be staged for processing. Validate the Brand frame and retry. {exception.Message}"));
        }
    }

    private static void TryDeleteStagedFrameDirectory(DirectoryReference directory)
    {
        try
        {
            if (Directory.Exists(directory.Value)) Directory.Delete(directory.Value, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    private static IReadOnlyList<ProductionPrefixSource> ValidateProductionPrefixSources(PrintableBookProcessingCommand command)
    {
        var sources = command.EffectiveProductionPrefixSources;
        if (command.Mode != BookProcessingMode.ProductionInterior)
        {
            if (sources.Count > 0)
            {
                throw new ArgumentException("Production prefix sources are only valid in Production Interior mode.", nameof(command));
            }
            return [];
        }

        var expected = new[] { ProductionAssetKind.InteriorCover, ProductionAssetKind.BookOwner };
        if (sources.Count != expected.Length ||
            !sources.Select(source => source.AssetKind).SequenceEqual(expected) ||
            sources.Any(source => string.IsNullOrWhiteSpace(source.PageId)))
        {
            throw new BookProcessingFailureException(
                "production-prefix-pages",
                new ProcessingFailure("production_interior_assets_missing", "Upload both Interior Cover and Book Owner Production assets before building Final Interior."));
        }
        return sources;
    }

    private async ValueTask RecordProductionInteriorStateAsync(
        BookWorkspace workspace,
        PrintableBookProcessingCommand command,
        IReadOnlyList<ProductionPrefixSource> sources,
        IReadOnlyList<InteriorPageProcessingResult> results,
        IReadOnlyList<InteriorSource> activeInteriorSources,
        InteriorShuffleMap shuffleMap,
        FileReference publishedInterior,
        FileReference? publishedPreview,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        if (productionStateStore is null || fileSystem is null)
        {
            return;
        }

        var state = await productionStateStore.LoadAsync(workspace, cancellationToken);
        var settingsSignature = ProductionPageProcessingService.CreateSettingsSignature(command);
        foreach (var source in sources)
        {
            var result = results.Single(item => string.Equals(item.PageId, source.PageId, StringComparison.Ordinal));
            var sourceMetadata = await fileSystem.GetFileMetadataAsync(source.Source, cancellationToken)
                ?? throw new FileNotFoundException("A Production source asset disappeared before state publication.", source.Source.Value);
            var outputMetadata = await fileSystem.GetFileMetadataAsync(result.FinalPage, cancellationToken)
                ?? throw new FileNotFoundException("A processed Production page disappeared before state publication.", result.FinalPage.Value);
            state = state.RecordProcessedPage(
                source.AssetKind,
                ProductionFileSignature.From(sourceMetadata),
                ProductionFileSignature.From(outputMetadata),
                settingsSignature,
                publishedAt);
        }

        IEnumerable<FileReference> inputFiles = sources.Select(source => source.Source)
            .Concat(command.EffectiveIntroTemplatePages)
            .Concat(activeInteriorSources.Select(source => source.Source));
        if (command.BackgroundPage is not null)
        {
            inputFiles = inputFiles.Append(command.BackgroundPage);
        }
        var inputSignature = await CreateProductionInteriorInputSignatureAsync(
            command,
            inputFiles,
            shuffleMap,
            cancellationToken);
        state = state.RecordInteriorOutput(
            Path.GetFileName(publishedInterior.Value),
            inputSignature,
            publishedAt,
            publishedPreview is null ? null : Path.GetFileName(publishedPreview.Value));
        await productionStateStore.SaveAsync(workspace, state, cancellationToken);
    }

    private async ValueTask<string> CreateProductionInteriorInputSignatureAsync(
        PrintableBookProcessingCommand command,
        IEnumerable<FileReference> files,
        InteriorShuffleMap shuffleMap,
        CancellationToken cancellationToken)
    {
        var parts = new List<string>
        {
            "production-interior-v1",
            CreateConfigurationFingerprint(command),
            $"shuffle:{shuffleMap.Seed}:{string.Join(',', shuffleMap.Entries.OrderBy(entry => entry.OutputIndex).Select(entry => $"{entry.OutputIndex}:{entry.Page.Value}"))}"
        };
        foreach (var file in files.OrderBy(file => file.Value, StringComparer.OrdinalIgnoreCase))
        {
            var metadata = await fileSystem!.GetFileMetadataAsync(file, cancellationToken)
                ?? throw new FileNotFoundException("A Production Interior input disappeared before state publication.", file.Value);
            parts.Add($"{file.Value}|{metadata.LengthBytes}|{metadata.LastWriteTimeUtc.UtcTicks}");
        }

        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts)))).ToLowerInvariant()}";
    }

    private static string CreateConfigurationFingerprint(PrintableBookProcessingCommand command) =>
        string.Join("|", command.PreparedArtworkSize.Width, command.PreparedArtworkSize.Height,
            command.WorkingPageSize.Width, command.WorkingPageSize.Height,
            command.FinalPageSize.Width, command.FinalPageSize.Height,
            command.TargetInteriorDensity.Horizontal, command.TargetInteriorDensity.Vertical,
            command.CoverPdfPageSize.WidthInches, command.CoverPdfPageSize.HeightInches,
            command.InteriorPdfPageSize.WidthInches, command.InteriorPdfPageSize.HeightInches,
            command.MaximumPageConcurrency, command.ArtworkDetectionThreshold.Value,
            command.Frame?.Value, command.Mode,
            command.CustomIntroFromBookInterior ? "CustomBookInterior" : "AutoBrand",
            string.Join(';', command.EffectiveIntroTemplatePages.Select(page => page.Value)));

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

    private sealed record StagedFrameAsset(DirectoryReference Directory, FileReference File, string Sha256);

    private sealed class BookProcessingFailureException(string step, ProcessingFailure failure) : Exception(failure.Message)
    {
        public string Step { get; } = step;

        public ProcessingFailure Failure { get; } = failure;
    }
}
