using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Production;

namespace PrintableBook.Core.Application.Desktop;

public sealed record BookValidationCheck(string Code, string Message, bool IsSuccess, bool IsWarning = false);
public sealed record InteriorPageSummary(string PageId, string Status, string FinalPagePath, string LocalImageUrl = "");
public sealed record InteriorSourcePageSummary(string SourceReference, FrameMode FrameMode, bool IsActive = true, string? SourceKey = null);
public sealed record BookFolderSummary(string Name, string Status, int FileCount, int ImageCount);
public sealed record BookAssetSummary(string SourceReference, string RelativePath, string FileName, string Folder, string Kind, int? Width, int? Height, FrameMode? FrameMode, string LocalImageUrl, bool IsActive = true);
public sealed record BookOutputSummary(
    string ArtifactReference,
    string FileName,
    long FileSizeBytes,
    int? PageCount,
    double? WidthInches,
    double? HeightInches,
    string VerificationStatus,
    DateTimeOffset? GeneratedAt,
    string ArtifactKind = "Unknown",
    string? PreviewArtifactReference = null,
    long? PreviewFileSizeBytes = null,
    string PreviewState = "Missing",
    DateTimeOffset? PreviewGeneratedAt = null,
    string? ThumbnailImageUrl = null);
public sealed record ProductionAssetDesktopSummary(
    string AssetKind,
    string DisplayName,
    string FileName,
    string SourceReference,
    string SourceStatus,
    string? SourceLocalImageUrl,
    long? SourceFileSizeBytes,
    DateTimeOffset? SourceUpdatedAtUtc,
    string? ProcessedReference,
    string ProcessedStatus,
    string? ProcessedLocalImageUrl,
    DateTimeOffset? ProcessedAtUtc);
public sealed record ProductionDesktopSummary(
    IReadOnlyList<ProductionAssetDesktopSummary> Assets,
    string CoverOutputStatus,
    DateTimeOffset? CoverBuiltAtUtc,
    string InteriorOutputStatus,
    string InteriorOutputKind,
    DateTimeOffset? InteriorBuiltAtUtc);
public interface ILocalOutputActionService
{
    ValueTask OpenAsync(FileReference file, CancellationToken cancellationToken = default);
    ValueTask OpenFolderAsync(DirectoryReference directory, CancellationToken cancellationToken = default);
    ValueTask RevealAsync(FileReference file, CancellationToken cancellationToken = default);
    ValueTask CopyPathAsync(FileReference file, CancellationToken cancellationToken = default);
}
public sealed record BookDesktopSummary(BookId BookId, string ValidationStatus, IReadOnlyList<BookValidationCheck> ValidationChecks, BookProcessingStatus WorkspaceStatus, string? CurrentStep, string? FailureMessage, IReadOnlyList<string> PublishedArtifacts, IReadOnlyList<InteriorPageSummary> InteriorPages, IReadOnlyList<BookProcessingLogEntry> Logs, int InteriorSourcePageCount, IReadOnlyList<BookFolderSummary>? SourceFolders = null, IReadOnlyList<string>? CoverCandidates = null, string? SelectedCoverReference = null, DateTimeOffset? LastRunAt = null, IReadOnlyList<InteriorSourcePageSummary>? InteriorSourcePages = null, IReadOnlyList<BookAssetSummary>? Assets = null, IReadOnlyList<BookValidationCheck>? FullBookValidationChecks = null, IReadOnlyList<BookOutputSummary>? OutputSummaries = null, string? RepresentativeCoverReference = null, bool HasBackground = true, int ActiveInteriorSourcePageCount = 0, bool HasIntro = false, IReadOnlyList<string>? SelectedIntroInteriorSourceKeys = null, ProductionDesktopSummary? Production = null, BookProductionMetadata? Metadata = null, string? AssignedBrand = null, BookBrandAssignmentStatus AssignmentStatus = BookBrandAssignmentStatus.Unassigned, string? AssignmentReason = null, bool WorkspaceStateAvailable = true, string? WorkspaceStateError = null, int LegacyFrameModePageCount = 0);
public sealed record BrandDesktopSummary(string BrandName, BrandValidationStatus ValidationStatus, DateTimeOffset? ValidatedAtUtc, string? Fingerprint, string? Author = null, string MetadataStatus = "Missing", string? MetadataError = null);
public sealed record ApplicationSnapshot(ApplicationDiscovery Discovery, GlobalSettings GlobalSettings, IReadOnlyList<BookDesktopSummary> BookSummaries, DateTimeOffset RefreshedAt, IReadOnlyList<BrandDesktopSummary>? BrandSummaries = null, IReadOnlyList<BrandImageSizeRequirement>? BrandImageSizeRequirements = null);

public interface IApplicationSnapshotService
{
    ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IApplicationSnapshotProvider
{
    ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationSnapshotService(
    IApplicationRootDiscovery discovery,
    IGlobalSettingsStore settingsStore,
    IBookSourceScanner sourceScanner,
    IBookWorkspaceStateStore stateStore,
    IFileSystem fileSystem,
    IPdfDocumentInspector? pdfDocumentInspector = null,
    IOperationDiagnostics? diagnostics = null,
    IBrandValidationService? brandValidationService = null,
    IProductionWorkspaceStateStore? productionStateStore = null,
    IBrandMetadataStore? brandMetadataStore = null,
    IInteriorShuffleStore? interiorShuffleStore = null) : IApplicationSnapshotService
{
    private const int MaximumBookSummaryConcurrency = 4;
    private readonly IOperationDiagnostics diagnostics = diagnostics ?? new NoOpOperationDiagnostics();

    public async ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var refreshOperation = diagnostics.Begin("snapshot.refresh");
        ApplicationDiscovery discoverySnapshot;
        using (diagnostics.Begin("discovery"))
        {
            discoverySnapshot = await discovery.DiscoverAsync(cancellationToken);
        }
        var settings = await settingsStore.LoadAsync(discoverySnapshot.Paths, cancellationToken);
        var brandSummaries = new List<BrandDesktopSummary>(discoverySnapshot.Brands.Count);
        var composedBrands = new List<DiscoveredBrand>(discoverySnapshot.Brands.Count);
        var assignmentTargets = new Dictionary<string, BrandAssignmentTarget>(StringComparer.Ordinal);
        foreach (var brand in discoverySnapshot.Brands)
        {
            BrandMetadata? metadata = null;
            var metadataStatus = "Missing";
            string? metadataError = null;
            if (brandMetadataStore is not null)
            {
                try
                {
                    metadata = await brandMetadataStore.LoadAsync(brand.Directory, cancellationToken);
                    metadataStatus = metadata is null ? "Missing" : "Available";
                }
                catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
                {
                    metadataStatus = "Unavailable";
                    metadataError = exception.Message;
                }
            }
            assignmentTargets.Add(brand.Name, new BrandAssignmentTarget(brand.Name, metadata, metadataStatus != "Unavailable"));

            BrandValidationState state;
            using (diagnostics.Begin("brand.state", brand.Name))
            {
                state = brandValidationService is null
                    ? new BrandValidationState(BrandValidationStatus.NotValidated)
                    : await brandValidationService.CheckStateAsync(brand.Directory, settings, cancellationToken);
            }
            brandSummaries.Add(new BrandDesktopSummary(brand.Name, state.Status, state.ValidatedAtUtc, state.Fingerprint, metadata?.Author, metadataStatus, metadataError));
            composedBrands.Add(ApplyValidatedBrandFacts(brand, state));
        }
        var brandsByName = composedBrands.ToDictionary(brand => brand.Name, StringComparer.Ordinal);
        var summaries = new BookDesktopSummary?[discoverySnapshot.Books.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, discoverySnapshot.Books.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaximumBookSummaryConcurrency,
                CancellationToken = cancellationToken
            },
            async (index, token) => summaries[index] = await BuildBookSummaryAsync(discoverySnapshot.Books[index], settings, assignmentTargets, brandsByName, token));

        var completedSummaries = summaries
            .Select(summary => summary ?? throw new InvalidOperationException("Book summary was not produced."))
            .ToArray();
        var composedDiscovery = discoverySnapshot with { Brands = composedBrands };
        return new ApplicationSnapshot(composedDiscovery, settings, completedSummaries, DateTimeOffset.UtcNow, brandSummaries, BrandValidationDefinition.GetImageSizeRequirements(settings));
    }

    private static DiscoveredBrand ApplyValidatedBrandFacts(DiscoveredBrand brand, BrandValidationState state)
    {
        if (state.Status != BrandValidationStatus.Validated ||
            state.ValidatedAssets is not { Count: > 0 } facts ||
            brand.Assets is null)
        {
            return brand;
        }

        var sizes = facts.ToDictionary(fact => fact.RelativePath, fact => fact.Size, StringComparer.Ordinal);
        var assets = brand.Assets.Select(asset =>
        {
            if (asset.Entries is not null)
            {
                var entries = asset.Entries.Select(entry =>
                {
                    var key = BrandValidationTargetResolver.NormalizeRelativePath(Path.Combine(asset.Name, entry.Name));
                    return sizes.TryGetValue(key, out var size) ? entry with { Size = size } : entry;
                }).ToArray();
                return asset with { Entries = entries };
            }

            var assetKey = BrandValidationTargetResolver.NormalizeRelativePath(asset.Name);
            return sizes.TryGetValue(assetKey, out var assetSize) ? asset with { Size = assetSize } : asset;
        }).ToArray();

        return brand with { Assets = assets };
    }

    private async ValueTask<BookDesktopSummary> BuildBookSummaryAsync(
        DiscoveredBook book,
        GlobalSettings settings,
        IReadOnlyDictionary<string, BrandAssignmentTarget> assignmentTargets,
        IReadOnlyDictionary<string, DiscoveredBrand> brandsByName,
        CancellationToken cancellationToken)
    {
        BookSourceScanResult scan;
        using (diagnostics.Begin("book.scan", book.Id.Value))
        {
            scan = await sourceScanner.ScanAsync(book.Id, book.Directory, cancellationToken);
        }
        var validation = scan.IsSuccess ? BookSourceValidator.Validate(scan.Source!) : null;
        var source = validation?.Source;
        var sourceFailure = scan.Failure ?? validation?.Failure;
        var isSourceValid = scan.IsSuccess && validation!.IsSuccess;
        var processingRoot = scan.Metadata?.ProcessingRoot ?? book.Directory;
        BookWorkspaceStateLoadResult stateLoad;
        var stateAvailable = true;
        string? stateError = null;
        try
        {
            stateLoad = await stateStore.LoadWithMetadataAsync(book.Workspace, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            stateAvailable = false;
            stateError = exception.Message;
            stateLoad = new(BookProcessingState.NotStarted(book.Id), BookProcessingState.CurrentFrameModeContractVersion, LegacyFrameContractDetected: false);
        }
        var state = stateLoad.State ?? BookProcessingState.NotStarted(book.Id);
        assignmentTargets.TryGetValue(state.AssignedBrand ?? string.Empty, out var assignmentTarget);
        brandsByName.TryGetValue(state.AssignedBrand ?? string.Empty, out var assignedBrand);
        var assignment = BookBrandAssignmentEvaluator.Evaluate(state.AssignedBrand, state.Metadata?.Author, assignmentTarget);
        var coverCandidates = source?.GetAssets(BookAssetKind.Cover).Select(asset => asset.Reference).ToArray() ?? [];
        var hasSelectedCover = coverCandidates.Length == 1 || coverCandidates.Any(candidate => string.Equals(candidate, state.SelectedCoverReference, StringComparison.OrdinalIgnoreCase));
        var interiorPages = (state.PublishedInteriorPreviews ?? [])
            .Select(preview => new InteriorPageSummary(preview.PageId, "Completed", preview.FinalPagePath, ToLocalImageUrl(preview.FinalPagePath)))
            .OrderBy(page => page.PageId, StringComparer.Ordinal)
            .ToArray();
        var checks = new List<BookValidationCheck>();
        if (!stateAvailable)
        {
            checks.Add(new BookValidationCheck("workspace_state_corrupt", stateError!, false));
        }
        if (isSourceValid)
        {
            checks.Add(new BookValidationCheck("book.interior_ready", "Interior source images were discovered.", true));
        }
        else
        {
            checks.Add(new BookValidationCheck(sourceFailure!.Code, sourceFailure.Message, false));
        }
        if (coverCandidates.Length == 0)
        {
            checks.Add(new BookValidationCheck(
                "book.cover_skipped",
                "Cover is unavailable and will be skipped for Interior-only processing.",
                true,
                true));
        }
        else if (coverCandidates.Length > 1)
        {
            checks.Add(new BookValidationCheck(
                "book.cover_selection_optional",
                hasSelectedCover ? "A cover candidate was selected." : "Cover selection is not required for Interior-only processing.",
                true,
                true));
        }
        var sourcePages = source?.GetAssets(BookAssetKind.Interior)
            .Select(asset =>
            {
                var sourceFile = new FileReference(asset.Reference);
                var sourceKey = InteriorSourceKey.FromBookRoot(book.Directory, sourceFile);
                return new InteriorSourcePageSummary(asset.Reference, state.GetInteriorFrameMode(sourceKey), state.IsInteriorActive(sourceKey), sourceKey);
            })
            .ToArray() ?? [];
        var selectedIntroKeys = state.SelectedIntroInteriorSourceKeys ?? [];
        var introKeys = state.HasIntro
            ? new HashSet<string>(selectedIntroKeys, StringComparer.OrdinalIgnoreCase)
            : [];
        var missingIntroKeys = state.HasIntro && selectedIntroKeys.Any(key => !sourcePages.Any(page => string.Equals(page.SourceKey, key, StringComparison.OrdinalIgnoreCase)));
        var needsIntroSelection = state.HasIntro && (selectedIntroKeys.Count == 0 || missingIntroKeys);
        var introCheck = new BookValidationCheck(
            missingIntroKeys ? "book.intro_selection_missing" : "book.intro_selection_required",
            missingIntroKeys ? "A selected custom Intro source is no longer available in Book interior." : "Choose at least one Book interior image before processing a custom Intro selection.",
            false);
        var fullBookChecks = new List<BookValidationCheck>
            {
                isSourceValid
                    ? new BookValidationCheck("book.interior_ready", "Interior source images were discovered.", true)
                    : new BookValidationCheck(sourceFailure!.Code, sourceFailure.Message, false)
            };
        if (coverCandidates.Length == 0)
        {
            fullBookChecks.Add(new BookValidationCheck(
                "book.cover_required",
                "A Cover PNG is required before this Book can be exported as a full book.",
                false));
        }
        else if (coverCandidates.Length > 1 && !hasSelectedCover)
        {
            fullBookChecks.Add(new BookValidationCheck(
                "book.cover_selection_required",
                "Choose one Cover PNG before this Book can be exported as a full book.",
                false));
        }
        else
        {
            fullBookChecks.Add(new BookValidationCheck(
                "book.cover_ready",
                "A Cover PNG is selected for full-book output.",
                true));
        }
        if (needsIntroSelection)
        {
            fullBookChecks.Add(introCheck);
        }
        var isReady = isSourceValid && stateAvailable;
        var normalInteriorPages = sourcePages.Where(page => !introKeys.Contains(page.SourceKey!)).ToArray();
        var explicitLegacyKeys = new HashSet<string>(stateLoad.ExplicitLegacyFrameSourceKeys ?? [], StringComparer.OrdinalIgnoreCase);
        var explicitAutoKeys = new HashSet<string>(stateLoad.ExplicitLegacyAutoSourceKeys ?? [], StringComparer.OrdinalIgnoreCase);
        var legacyFrameModePageCount = stateLoad.LegacyFrameContractDetected
            ? normalInteriorPages.Count(page => explicitAutoKeys.Contains(page.SourceKey!) || !explicitLegacyKeys.Contains(page.SourceKey!))
            : 0;
        if (legacyFrameModePageCount > 0)
        {
            checks.Add(new BookValidationCheck(
                "book.frame_mode_migrated",
                $"{legacyFrameModePageCount} Interior page(s) previously using Auto now use No Frame. Review Interior artwork before reprocessing.",
                true,
                true));
        }
        var activeInteriorSourcePageCount = normalInteriorPages.Count(page => page.IsActive);
        if (isSourceValid && activeInteriorSourcePageCount == 0)
        {
            isReady = false;
            checks.Add(new BookValidationCheck("book.no_active_interior_pages", "Activate at least one Interior page before processing.", false));
        }
        if (needsIntroSelection)
        {
            checks.Add(introCheck);
        }
        var representativeCoverReference = scan.Metadata?.RepresentativeImageReference?.Value ??
            FindRepresentativeCoverReference(processingRoot, source, state.SelectedCoverReference);
        var assetSummaries = DescribeAssets(book, source, state, scan.Metadata?.RepresentativeImageReference);
        return new BookDesktopSummary(
            book.Id,
            !isReady ? "Invalid" : needsIntroSelection ? "Needs review" : "Ready",
            checks,
            state.Status,
            state.CurrentStep,
            state.Failure?.Message,
            state.PublishedArtifactReferences ?? [],
            interiorPages,
            await stateStore.LoadLogsAsync(book.Workspace, cancellationToken),
            source?.GetAssets(BookAssetKind.Interior).Count ?? 0,
            await DiscoverSourceFoldersAsync(processingRoot, cancellationToken),
            coverCandidates,
            state.SelectedCoverReference,
            state.UpdatedAt == DateTimeOffset.MinValue ? null : state.UpdatedAt,
            sourcePages,
            assetSummaries,
            fullBookChecks,
            await DescribeOutputsAsync(book.Id, state.PublishedArtifactReferences ?? [], state, cancellationToken),
            representativeCoverReference,
            HasBackground: state.HasBackground,
            ActiveInteriorSourcePageCount: activeInteriorSourcePageCount,
            HasIntro: state.HasIntro,
            SelectedIntroInteriorSourceKeys: state.SelectedIntroInteriorSourceKeys,
            Production: await DescribeProductionAsync(book, state, settings, source, assignedBrand, cancellationToken),
            Metadata: state.Metadata,
            AssignedBrand: state.AssignedBrand,
            AssignmentStatus: assignment.Status,
            AssignmentReason: assignment.Reason,
            WorkspaceStateAvailable: stateAvailable,
            WorkspaceStateError: stateError,
            LegacyFrameModePageCount: legacyFrameModePageCount);
    }

    private async ValueTask<ProductionDesktopSummary> DescribeProductionAsync(
        DiscoveredBook book,
        BookProcessingState bookState,
        GlobalSettings settings,
        BookSource? bookSource,
        DiscoveredBrand? assignedBrand,
        CancellationToken cancellationToken)
    {
        var workspace = book.Workspace;
        var productionState = productionStateStore is null
            ? ProductionWorkspaceState.Empty
            : await productionStateStore.LoadAsync(workspace, cancellationToken);
        var settingsSignature = ProductionPageProcessingService.CreateSettingsSignature(settings);
        var assets = new List<ProductionAssetDesktopSummary>(ProductionAssets.All.Count);

        foreach (var definition in ProductionAssets.All)
        {
            var source = ProductionWorkspacePaths.SourceFile(workspace, definition.Kind);
            var sourceMetadata = await fileSystem.GetFileMetadataAsync(source, cancellationToken);
            ProductionFileSignature? sourceSignature = sourceMetadata is null
                ? null
                : ProductionFileSignature.From(sourceMetadata.Value);
            var processedReference = definition.ProcessedFileName is null
                ? null
                : ProductionWorkspacePaths.ProcessedFile(workspace, definition.Kind);
            var processedMetadata = processedReference is null
                ? null
                : await fileSystem.GetFileMetadataAsync(processedReference, cancellationToken);
            var processedState = productionState.ProcessedPages is not null &&
                productionState.ProcessedPages.TryGetValue(definition.FileName, out var savedProcessed)
                    ? savedProcessed
                    : null;

            var processedStatus = "Not applicable";
            if (definition.ProcessedFileName is not null)
            {
                processedStatus = sourceMetadata is null
                    ? "Missing"
                    : processedMetadata is null || processedState is null
                        ? "Ready to process"
                        : processedState.SourceSignature != sourceSignature ||
                          processedState.OutputSignature != ProductionFileSignature.From(processedMetadata.Value) ||
                          !string.Equals(processedState.ProcessingSettingsSignature, settingsSignature, StringComparison.Ordinal)
                            ? "Stale"
                            : "Processed";
            }

            assets.Add(new ProductionAssetDesktopSummary(
                ToAssetKindValue(definition.Kind),
                definition.Kind switch
                {
                    ProductionAssetKind.FinalCover => "Final Cover",
                    ProductionAssetKind.InteriorCover => "Interior Cover",
                    ProductionAssetKind.BookOwner => "Book Owner",
                    _ => definition.FileName
                },
                definition.FileName,
                source.Value,
                sourceMetadata is null ? "Missing" : "Ready to process",
                sourceMetadata is null ? null : ToVersionedLocalImageUrl(source.Value, sourceMetadata.Value),
                sourceMetadata?.LengthBytes,
                sourceMetadata?.LastWriteTimeUtc,
                processedReference?.Value,
                processedStatus,
                processedMetadata is null || processedReference is null ? null : ToVersionedLocalImageUrl(processedReference.Value, processedMetadata.Value),
                processedState?.CompletedAtUtc));
        }

        var cover = assets.Single(asset => asset.AssetKind == "final-cover");
        var coverOutputStatus = cover.SourceStatus == "Missing"
            ? "Missing"
            : productionState.CoverOutput is null
                ? "Ready to process"
                : CreateCurrentCoverSignature(cover) == productionState.CoverOutput.InputSignature ? "Processed" : "Stale";
        var interiorKind = bookState.PublishedInteriorKind switch
        {
            InteriorOutputKind.Base => "Base",
            InteriorOutputKind.Production => "Production",
            _ => "Legacy"
        };
        var productionInteriorAssets = assets.Where(asset => asset.AssetKind is "interior-cover" or "book-owner").ToArray();
        var currentInteriorSignature = productionState.InteriorOutput is null
            ? null
            : await TryCreateCurrentProductionInteriorSignatureAsync(
                book,
                bookState,
                settings,
                bookSource,
                assignedBrand,
                cancellationToken);
        var publishedInteriorReference = (bookState.PublishedArtifactReferences ?? [])
            .FirstOrDefault(reference => Path.GetFileName(reference).EndsWith(" - Interior.pdf", StringComparison.OrdinalIgnoreCase));
        var publishedInteriorExists = publishedInteriorReference is not null &&
            await fileSystem.FileExistsAsync(new FileReference(publishedInteriorReference), cancellationToken);
        var interiorOutputStatus = productionInteriorAssets.Any(asset => asset.SourceStatus == "Missing")
            ? "Missing"
            : productionState.InteriorOutput is null
                ? "Ready to process"
            : assets.Where(asset => asset.AssetKind is "interior-cover" or "book-owner")
                .Any(asset => asset.ProcessedStatus != "Processed")
                ? "Stale"
            : !ProductionInteriorSignature.IsCurrent(productionState.InteriorOutput.InputSignature) ||
              currentInteriorSignature is null ||
              !string.Equals(currentInteriorSignature, productionState.InteriorOutput.InputSignature, StringComparison.Ordinal) ||
              !publishedInteriorExists
                ? "Stale"
                : "Processed";
        return new ProductionDesktopSummary(
            assets,
            coverOutputStatus,
            productionState.CoverOutput?.CompletedAtUtc,
            interiorOutputStatus,
            interiorKind,
            bookState.PublishedInteriorAtUtc);
    }

    private async ValueTask<string?> TryCreateCurrentProductionInteriorSignatureAsync(
        DiscoveredBook book,
        BookProcessingState state,
        GlobalSettings settings,
        BookSource? source,
        DiscoveredBrand? assignedBrand,
        CancellationToken cancellationToken)
    {
        if (interiorShuffleStore is null || source is null || assignedBrand is null)
        {
            return null;
        }

        var prefixFacts = new List<ProductionInteriorFileFact>(2);
        foreach (var kind in new[] { ProductionAssetKind.InteriorCover, ProductionAssetKind.BookOwner })
        {
            var fact = await TryCreateProductionInteriorFileFactAsync(
                kind.ToString(),
                ProductionWorkspacePaths.SourceFile(book.Workspace, kind),
                cancellationToken);
            if (fact is null) return null;
            prefixFacts.Add(fact);
        }

        var allInteriorSources = source.GetAssets(BookAssetKind.Interior)
            .Select(asset =>
            {
                var file = new FileReference(asset.Reference);
                return new
                {
                    File = file,
                    SourceKey = InteriorSourceKey.FromBookRoot(book.Directory, file)
                };
            })
            .ToArray();
        var selectedIntroKeys = state.HasIntro
            ? new HashSet<string>(state.SelectedIntroInteriorSourceKeys ?? [], StringComparer.OrdinalIgnoreCase)
            : [];
        IReadOnlyList<FileReference> introFiles;
        if (state.HasIntro)
        {
            var byKey = allInteriorSources.ToDictionary(item => item.SourceKey, item => item.File, StringComparer.OrdinalIgnoreCase);
            var selected = new List<FileReference>(selectedIntroKeys.Count);
            foreach (var key in state.SelectedIntroInteriorSourceKeys ?? [])
            {
                if (!byKey.TryGetValue(key, out var file)) return null;
                selected.Add(file);
            }
            introFiles = selected;
        }
        else
        {
            var selection = IntroTemplateSelectionResolver.Resolve(assignedBrand.IntroTemplateAssets);
            if (!selection.IsSuccess) return null;
            introFiles = selection.Assets.Select(asset => new FileReference(asset.SourceReference)).ToArray();
        }

        var introFacts = new List<ProductionInteriorFileFact>(introFiles.Count);
        foreach (var file in introFiles)
        {
            var fact = await TryCreateProductionInteriorFileFactAsync("intro", file, cancellationToken);
            if (fact is null) return null;
            introFacts.Add(fact);
        }

        var activeInteriorSources = allInteriorSources
            .Where(item => !selectedIntroKeys.Contains(item.SourceKey) && state.IsInteriorActive(item.SourceKey))
            .ToArray();
        if (activeInteriorSources.Length == 0) return null;
        var interiorFacts = new List<ProductionInteriorPageFact>(activeInteriorSources.Length);
        foreach (var item in activeInteriorSources)
        {
            var fact = await TryCreateProductionInteriorFileFactAsync("interior", item.File, cancellationToken);
            if (fact is null) return null;
            interiorFacts.Add(new ProductionInteriorPageFact(item.SourceKey, fact, state.GetInteriorFrameMode(item.SourceKey)));
        }

        var shuffleMap = await interiorShuffleStore.LoadAsync(book.Workspace, cancellationToken);
        if (shuffleMap is null ||
            !shuffleMap.Entries.Select(entry => entry.Page.Value).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(activeInteriorSources.Select(item => item.File.Value).OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        ProductionInteriorFileFact? frameFact = null;
        if (interiorFacts.Any(page => page.FrameMode == FrameMode.Enabled))
        {
            frameFact = await TryCreateProductionInteriorFileFactAsync(
                "frame",
                new FileReference(Path.Combine(assignedBrand.Directory.Value, "frame.png")),
                cancellationToken);
            if (frameFact is null) return null;
        }

        ProductionInteriorFileFact? backgroundFact = null;
        if (state.HasBackground)
        {
            backgroundFact = await TryCreateProductionInteriorFileFactAsync(
                "background",
                new FileReference(Path.Combine(assignedBrand.Directory.Value, "background.png")),
                cancellationToken);
            if (backgroundFact is null) return null;
        }

        return ProductionInteriorSignature.Create(new ProductionInteriorSignatureRecipe(
            ProductionInteriorSignature.CreateRenderingSignature(settings),
            prefixFacts,
            introFacts,
            interiorFacts,
            shuffleMap,
            frameFact,
            state.HasBackground,
            backgroundFact));
    }

    private async ValueTask<ProductionInteriorFileFact?> TryCreateProductionInteriorFileFactAsync(
        string role,
        FileReference file,
        CancellationToken cancellationToken)
    {
        var metadata = await fileSystem.GetFileMetadataAsync(file, cancellationToken);
        return metadata is null
            ? null
            : new ProductionInteriorFileFact(role, file.Value, ProductionFileSignature.From(metadata.Value));
    }

    private static string CreateCurrentCoverSignature(ProductionAssetDesktopSummary cover)
    {
        var signature = new ProductionFileSignature(
            cover.SourceFileSizeBytes!.Value,
            cover.SourceUpdatedAtUtc!.Value);
        return ProductionCoverPdfService.CreateInputSignature(signature);
    }

    private static string ToAssetKindValue(ProductionAssetKind kind) => kind switch
    {
        ProductionAssetKind.FinalCover => "final-cover",
        ProductionAssetKind.InteriorCover => "interior-cover",
        ProductionAssetKind.BookOwner => "book-owner",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported Production asset kind.")
    };

    private static string? FindRepresentativeCoverReference(DirectoryReference processingRoot, BookSource? source, string? selectedCoverReference)
    {
        var covers = source?.GetAssets(BookAssetKind.Cover) ?? [];
        return covers
            .OrderByDescending(asset => string.Equals(asset.Reference, selectedCoverReference, StringComparison.OrdinalIgnoreCase))
            .ThenBy(asset => IsBookCoverAsset(processingRoot, asset) ? 0 : 1)
            .ThenBy(asset => asset.Reference, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Reference;
    }

    private static bool IsBookCoverAsset(DirectoryReference processingRoot, BookAsset asset) =>
        Path.GetRelativePath(processingRoot.Value, asset.Reference)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .StartsWith($"Book cover{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<BookAssetSummary> DescribeAssets(
        DiscoveredBook book,
        BookSource? source,
        BookProcessingState state,
        FileReference? representativeImage)
    {
        var sourceAssets = source?.Assets ?? [];
        var summaries = new List<BookAssetSummary>(sourceAssets.Count + (representativeImage is null ? 0 : 1));
        foreach (var asset in sourceAssets)
        {
            var file = new FileReference(asset.Reference);
            var relativePath = Path.GetRelativePath(book.Directory.Value, asset.Reference);
            var folder = Path.GetDirectoryName(relativePath) ?? string.Empty;
            var sourceKey = asset.Kind == BookAssetKind.Interior ? InteriorSourceKey.FromBookRoot(book.Directory, file) : null;
            summaries.Add(new BookAssetSummary(
                asset.Reference,
                relativePath,
                Path.GetFileName(asset.Reference),
                folder,
                asset.Kind.ToString(),
                null,
                null,
                sourceKey is null ? null : state.GetInteriorFrameMode(sourceKey),
                ToLocalImageUrl(asset.Reference),
                sourceKey is null || state.IsInteriorActive(sourceKey)));
        }

        if (representativeImage is not null &&
            !summaries.Any(asset => string.Equals(asset.SourceReference, representativeImage.Value, StringComparison.OrdinalIgnoreCase)))
        {
            var relativePath = Path.GetRelativePath(book.Directory.Value, representativeImage.Value);
            summaries.Add(new BookAssetSummary(
                representativeImage.Value,
                relativePath,
                Path.GetFileName(representativeImage.Value),
                Path.GetDirectoryName(relativePath) ?? string.Empty,
                "Representative",
                null,
                null,
                null,
                ToLocalImageUrl(representativeImage.Value)));
        }

        return summaries;
    }

    private static string ToLocalImageUrl(string sourceReference) =>
        new Uri(Path.GetFullPath(sourceReference)).AbsoluteUri;

    private static string ToVersionedLocalImageUrl(string sourceReference, FileMetadata metadata) =>
        $"{ToLocalImageUrl(sourceReference)}?v={metadata.LengthBytes}-{metadata.LastWriteTimeUtc.UtcTicks}";

    private async ValueTask<IReadOnlyList<BookOutputSummary>> DescribeOutputsAsync(
        BookId bookId,
        IReadOnlyList<string> artifacts,
        BookProcessingState state,
        CancellationToken cancellationToken)
    {
        var outputs = new List<BookOutputSummary>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(artifact);
            var artifactKind = GetArtifactKind(info.Name);
            if (!info.Exists)
            {
                outputs.Add(new BookOutputSummary(
                    artifact,
                    Path.GetFileName(artifact),
                    0,
                    null,
                    null,
                    null,
                    "Missing",
                    null,
                    artifactKind));
                continue;
            }

            try
            {
                PdfDocumentInspection? inspection = null;
                if (pdfDocumentInspector is not null)
                {
                    using var inspectionOperation = diagnostics.Begin("pdf.inspect", $"{bookId.Value}/{Path.GetFileName(artifact)}");
                    inspection = await pdfDocumentInspector.InspectAsync(new FileReference(artifact), cancellationToken);
                }
                var previewReference = artifactKind switch
                {
                    "Cover" => state.PublishedCoverPreviewReference,
                    "Interior" => state.PublishedInteriorPreviewReference,
                    _ => null
                };
                var preview = await DescribePreviewAsync(
                    artifact,
                    info,
                    inspection,
                    previewReference,
                    cancellationToken);
                var thumbnailImageUrl = await DescribeCoverThumbnailImageUrlAsync(
                    artifact,
                    info,
                    artifactKind,
                    cancellationToken);
                outputs.Add(new BookOutputSummary(
                    artifact,
                    info.Name,
                    info.Length,
                    inspection?.PageCount,
                    inspection?.FirstPageSize.WidthInches,
                    inspection?.FirstPageSize.HeightInches,
                    inspection is null ? "Available" : "Verified",
                    new DateTimeOffset(info.LastWriteTimeUtc),
                    artifactKind,
                    preview.Reference,
                    preview.FileSizeBytes,
                    preview.State,
                    preview.GeneratedAt,
                    thumbnailImageUrl));
            }
            catch (Exception)
            {
                outputs.Add(new BookOutputSummary(
                    artifact,
                    info.Name,
                    info.Length,
                    null,
                    null,
                    null,
                    "Invalid",
                    new DateTimeOffset(info.LastWriteTimeUtc),
                    artifactKind,
                    PreviewState: "Invalid"));
            }
        }
        return outputs;
    }

    private async ValueTask<OutputPreviewDescription> DescribePreviewAsync(
        string artifact,
        FileInfo mainInfo,
        PdfDocumentInspection? mainInspection,
        string? previewReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previewReference)) return OutputPreviewDescription.Missing;

        if (!IsExpectedPreviewReference(artifact, previewReference))
        {
            return new OutputPreviewDescription(null, null, "Stale", null);
        }

        var previewInfo = new FileInfo(previewReference);
        if (!previewInfo.Exists) return OutputPreviewDescription.Missing;
        if (previewInfo.Length >= mainInfo.Length)
        {
            return new OutputPreviewDescription(null, previewInfo.Length, "Invalid", new DateTimeOffset(previewInfo.LastWriteTimeUtc));
        }

        try
        {
            if (pdfDocumentInspector is not null)
            {
                using var inspectionOperation = diagnostics.Begin("pdf.inspect", $"preview/{previewInfo.Name}");
                var previewInspection = await pdfDocumentInspector.InspectAsync(new FileReference(previewReference), cancellationToken);
                if (mainInspection is not null &&
                    (previewInspection.PageCount != mainInspection.PageCount ||
                     Math.Abs(previewInspection.FirstPageSize.WidthInches - mainInspection.FirstPageSize.WidthInches) > 0.001 ||
                     Math.Abs(previewInspection.FirstPageSize.HeightInches - mainInspection.FirstPageSize.HeightInches) > 0.001))
                {
                    return new OutputPreviewDescription(null, previewInfo.Length, "Invalid", new DateTimeOffset(previewInfo.LastWriteTimeUtc));
                }
            }

            return new OutputPreviewDescription(
                previewReference,
                previewInfo.Length,
                "Ready",
                new DateTimeOffset(previewInfo.LastWriteTimeUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new OutputPreviewDescription(null, previewInfo.Length, "Invalid", new DateTimeOffset(previewInfo.LastWriteTimeUtc));
        }
    }

    private async ValueTask<string?> DescribeCoverThumbnailImageUrlAsync(
        string artifact,
        FileInfo mainInfo,
        string artifactKind,
        CancellationToken cancellationToken)
    {
        if (artifactKind != "Cover") return null;

        var thumbnail = new FileReference(Path.Combine(
            Path.GetDirectoryName(artifact) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(artifact)}_thumbnail.png"));
        var metadata = await fileSystem.GetFileMetadataAsync(thumbnail, cancellationToken);
        if (metadata is null || metadata.Value.LastWriteTimeUtc < new DateTimeOffset(mainInfo.LastWriteTimeUtc)) return null;
        return ToVersionedLocalImageUrl(thumbnail.Value, metadata.Value);
    }

    private static string GetArtifactKind(string fileName) =>
        fileName.EndsWith(" - Cover.pdf", StringComparison.OrdinalIgnoreCase)
            ? "Cover"
            : fileName.EndsWith(" - Interior.pdf", StringComparison.OrdinalIgnoreCase)
                ? "Interior"
                : "Unknown";

    private static bool IsExpectedPreviewReference(string artifact, string previewReference)
    {
        try
        {
            var expectedReference = Path.Combine(
                Path.GetDirectoryName(artifact) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(artifact)}_thumbnail.pdf");
            return string.Equals(
                Path.GetFullPath(previewReference),
                Path.GetFullPath(expectedReference),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private sealed record OutputPreviewDescription(
        string? Reference,
        long? FileSizeBytes,
        string State,
        DateTimeOffset? GeneratedAt)
    {
        public static OutputPreviewDescription Missing { get; } = new(null, null, "Missing", null);
    }

    private async ValueTask<IReadOnlyList<BookFolderSummary>> DiscoverSourceFoldersAsync(DirectoryReference bookDirectory, CancellationToken cancellationToken)
    {
        var folders = new List<BookFolderSummary>(BookSourceLayout.KnownFolderNames.Count);
        foreach (var name in BookSourceLayout.KnownFolderNames)
        {
            var directory = new DirectoryReference(Path.Combine(bookDirectory.Value, name));
            if (!await fileSystem.DirectoryExistsAsync(directory, cancellationToken))
            {
                folders.Add(new BookFolderSummary(name, "Missing", 0, 0));
                continue;
            }

            var fileCount = 0;
            var imageCount = 0;
            await foreach (var file in fileSystem.EnumerateFilesAsync(directory, cancellationToken))
            {
                fileCount++;
                if (BookSourceLayout.IsSupportedImage(file.Value)) imageCount++;
            }
            folders.Add(new BookFolderSummary(name, "Present", fileCount, imageCount));
        }
        return folders;
    }
}
