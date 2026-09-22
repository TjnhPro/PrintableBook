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
public sealed record BookAssetSummary(string SourceReference, string RelativePath, string FileName, string Folder, string Kind, int? Width, int? Height, FrameMode FrameMode, string LocalImageUrl, bool IsActive = true);
public sealed record BookOutputSummary(string ArtifactReference, string FileName, long FileSizeBytes, int? PageCount, double? WidthInches, double? HeightInches, string VerificationStatus, DateTimeOffset? GeneratedAt);
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
    ValueTask RevealAsync(FileReference file, CancellationToken cancellationToken = default);
    ValueTask CopyPathAsync(FileReference file, CancellationToken cancellationToken = default);
}
public sealed record BookDesktopSummary(BookId BookId, string ValidationStatus, IReadOnlyList<BookValidationCheck> ValidationChecks, BookProcessingStatus WorkspaceStatus, string? CurrentStep, string? FailureMessage, IReadOnlyList<string> PublishedArtifacts, IReadOnlyList<InteriorPageSummary> InteriorPages, IReadOnlyList<BookProcessingLogEntry> Logs, int InteriorSourcePageCount, IReadOnlyList<BookFolderSummary>? SourceFolders = null, IReadOnlyList<string>? CoverCandidates = null, string? SelectedCoverReference = null, DateTimeOffset? LastRunAt = null, IReadOnlyList<InteriorSourcePageSummary>? InteriorSourcePages = null, IReadOnlyList<BookAssetSummary>? Assets = null, IReadOnlyList<BookValidationCheck>? FullBookValidationChecks = null, IReadOnlyList<BookOutputSummary>? OutputSummaries = null, string? RepresentativeCoverReference = null, bool HasBackground = true, int ActiveInteriorSourcePageCount = 0, bool HasIntro = false, IReadOnlyList<string>? SelectedIntroInteriorSourceKeys = null, ProductionDesktopSummary? Production = null, BookProductionMetadata? Metadata = null, string? AssignedBrand = null, BookBrandAssignmentStatus AssignmentStatus = BookBrandAssignmentStatus.Unassigned, string? AssignmentReason = null);
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
    IBrandMetadataStore? brandMetadataStore = null) : IApplicationSnapshotService
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
        var summaries = new BookDesktopSummary?[discoverySnapshot.Books.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, discoverySnapshot.Books.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaximumBookSummaryConcurrency,
                CancellationToken = cancellationToken
            },
            async (index, token) => summaries[index] = await BuildBookSummaryAsync(discoverySnapshot.Books[index], settings, assignmentTargets, token));

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
        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        assignmentTargets.TryGetValue(state.AssignedBrand ?? string.Empty, out var assignmentTarget);
        var assignment = BookBrandAssignmentEvaluator.Evaluate(state.AssignedBrand, state.Metadata?.Author, assignmentTarget);
        var coverCandidates = source?.GetAssets(BookAssetKind.Cover).Select(asset => asset.Reference).ToArray() ?? [];
        var hasSelectedCover = coverCandidates.Length == 1 || coverCandidates.Any(candidate => string.Equals(candidate, state.SelectedCoverReference, StringComparison.OrdinalIgnoreCase));
        var interiorPages = (state.PublishedInteriorPreviews ?? [])
            .Select(preview => new InteriorPageSummary(preview.PageId, "Completed", preview.FinalPagePath, ToLocalImageUrl(preview.FinalPagePath)))
            .OrderBy(page => page.PageId, StringComparer.Ordinal)
            .ToArray();
        var checks = new List<BookValidationCheck>();
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
        var isReady = isSourceValid;
        var normalInteriorPages = sourcePages.Where(page => !introKeys.Contains(page.SourceKey!)).ToArray();
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
            await DescribeOutputsAsync(book.Id, state.PublishedArtifactReferences ?? [], cancellationToken),
            representativeCoverReference,
            HasBackground: state.HasBackground,
            ActiveInteriorSourcePageCount: activeInteriorSourcePageCount,
            HasIntro: state.HasIntro,
            SelectedIntroInteriorSourceKeys: state.SelectedIntroInteriorSourceKeys,
            Production: await DescribeProductionAsync(book.Workspace, state, settings, cancellationToken),
            Metadata: state.Metadata,
            AssignedBrand: state.AssignedBrand,
            AssignmentStatus: assignment.Status,
            AssignmentReason: assignment.Reason);
    }

    private async ValueTask<ProductionDesktopSummary> DescribeProductionAsync(
        BookWorkspace workspace,
        BookProcessingState bookState,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
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
        var interiorOutputStatus = productionInteriorAssets.Any(asset => asset.SourceStatus == "Missing")
            ? "Missing"
            : productionState.InteriorOutput is null
                ? "Ready to process"
            : assets.Where(asset => asset.AssetKind is "interior-cover" or "book-owner")
                .Any(asset => asset.ProcessedStatus != "Processed")
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
                sourceKey is null ? FrameMode.Auto : state.GetInteriorFrameMode(sourceKey),
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
                FrameMode.Auto,
                ToLocalImageUrl(representativeImage.Value)));
        }

        return summaries;
    }

    private static string ToLocalImageUrl(string sourceReference) =>
        new Uri(Path.GetFullPath(sourceReference)).AbsoluteUri;

    private static string ToVersionedLocalImageUrl(string sourceReference, FileMetadata metadata) =>
        $"{ToLocalImageUrl(sourceReference)}?v={metadata.LengthBytes}-{metadata.LastWriteTimeUtc.UtcTicks}";

    private async ValueTask<IReadOnlyList<BookOutputSummary>> DescribeOutputsAsync(BookId bookId, IReadOnlyList<string> artifacts, CancellationToken cancellationToken)
    {
        var outputs = new List<BookOutputSummary>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(artifact);
            if (!info.Exists)
            {
                outputs.Add(new BookOutputSummary(artifact, Path.GetFileName(artifact), 0, null, null, null, "Missing", null));
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
                outputs.Add(new BookOutputSummary(artifact, info.Name, info.Length, inspection?.PageCount, inspection?.FirstPageSize.WidthInches, inspection?.FirstPageSize.HeightInches, inspection is null ? "Available" : "Verified", new DateTimeOffset(info.LastWriteTimeUtc)));
            }
            catch (Exception)
            {
                outputs.Add(new BookOutputSummary(artifact, info.Name, info.Length, null, null, null, "Invalid", new DateTimeOffset(info.LastWriteTimeUtc)));
            }
        }
        return outputs;
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
