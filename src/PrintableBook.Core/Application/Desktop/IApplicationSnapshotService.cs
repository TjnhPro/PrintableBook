using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Core.Application.Storage;

namespace PrintableBook.Core.Application.Desktop;

public sealed record BookValidationCheck(string Code, string Message, bool IsSuccess, bool IsWarning = false);
public sealed record InteriorPageSummary(string PageId, string Status, string FinalPagePath);
public sealed record InteriorSourcePageSummary(string SourceReference, FrameMode FrameMode, bool IsActive = true);
public sealed record BookFolderSummary(string Name, string Status, int FileCount, int ImageCount);
public sealed record BookAssetSummary(string SourceReference, string RelativePath, string FileName, string Folder, string Kind, int? Width, int? Height, FrameMode FrameMode, string LocalImageUrl, bool IsActive = true);
public sealed record BookOutputSummary(string ArtifactReference, string FileName, long FileSizeBytes, int? PageCount, double? WidthInches, double? HeightInches, string VerificationStatus, DateTimeOffset? GeneratedAt);
public interface ILocalOutputActionService
{
    ValueTask OpenAsync(FileReference file, CancellationToken cancellationToken = default);
    ValueTask RevealAsync(FileReference file, CancellationToken cancellationToken = default);
    ValueTask CopyPathAsync(FileReference file, CancellationToken cancellationToken = default);
}
public sealed record BookDesktopSummary(BookId BookId, string ValidationStatus, IReadOnlyList<BookValidationCheck> ValidationChecks, BookProcessingStatus WorkspaceStatus, string? CurrentStep, string? FailureMessage, IReadOnlyList<string> PublishedArtifacts, IReadOnlyList<InteriorPageSummary> InteriorPages, IReadOnlyList<BookProcessingLogEntry> Logs, int InteriorSourcePageCount, IReadOnlyList<BookFolderSummary>? SourceFolders = null, IReadOnlyList<string>? CoverCandidates = null, string? SelectedCoverReference = null, DateTimeOffset? LastRunAt = null, IReadOnlyList<InteriorSourcePageSummary>? InteriorSourcePages = null, IReadOnlyList<BookAssetSummary>? Assets = null, IReadOnlyList<BookValidationCheck>? FullBookValidationChecks = null, IReadOnlyList<BookOutputSummary>? OutputSummaries = null, string? RepresentativeCoverReference = null, long FolderSizeBytes = 0, bool HasBackground = false, int ActiveInteriorSourcePageCount = 0);
public sealed record ApplicationSnapshot(ApplicationDiscovery Discovery, GlobalSettings GlobalSettings, IReadOnlyList<BookDesktopSummary> BookSummaries, DateTimeOffset RefreshedAt);

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
    IBookStorageMaintenance storageMaintenance,
    IImageInspector? imageInspector = null,
    IPdfDocumentInspector? pdfDocumentInspector = null,
    IOperationDiagnostics? diagnostics = null) : IApplicationSnapshotService
{
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
        var summaries = new List<BookDesktopSummary>(discoverySnapshot.Books.Count);
        foreach (var book in discoverySnapshot.Books)
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
            var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
            var coverCandidates = source?.GetAssets(BookAssetKind.Cover).Select(asset => asset.Reference).ToArray() ?? [];
            var hasSelectedCover = coverCandidates.Length == 1 || coverCandidates.Any(candidate => string.Equals(candidate, state.SelectedCoverReference, StringComparison.OrdinalIgnoreCase));
            var interiorDirectory = new DirectoryReference(Path.Combine(book.Workspace.ProcessedDirectory.Value, "interior"));
            var interiorPages = new List<InteriorPageSummary>();
            await foreach (var page in fileSystem.EnumerateFilesAsync(interiorDirectory, cancellationToken))
            {
                if (string.Equals(Path.GetExtension(page.Value), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    interiorPages.Add(new InteriorPageSummary(Path.GetFileNameWithoutExtension(page.Value), "Completed", page.Value));
                }
            }
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
            var isReady = isSourceValid;
            var sourcePages = source?.GetAssets(BookAssetKind.Interior)
                .Select(asset =>
                {
                    var sourceFile = new FileReference(asset.Reference);
                    var sourceKey = InteriorSourceKey.FromBookRoot(book.Directory, sourceFile);
                    return new InteriorSourcePageSummary(asset.Reference, state.GetInteriorFrameMode(sourceKey), state.IsInteriorActive(sourceKey));
                })
                .ToArray() ?? [];
            var activeInteriorSourcePageCount = sourcePages.Count(page => page.IsActive);
            if (isSourceValid && activeInteriorSourcePageCount == 0)
            {
                isReady = false;
                checks.Add(new BookValidationCheck("book.no_active_interior_pages", "Activate at least one Interior page before processing.", false));
            }
            var assetSummaries = await DescribeAssetsAsync(book, source, state, cancellationToken);
            var folderSizeBytes = await storageMaintenance.GetBookSizeBytesAsync(book.Directory, cancellationToken);
            summaries.Add(new BookDesktopSummary(
                book.Id,
                isReady ? "Ready" : "Invalid",
                checks,
                state.Status,
                state.CurrentStep,
                state.Failure?.Message,
                state.PublishedArtifactReferences ?? [],
                interiorPages.OrderBy(page => page.PageId, StringComparer.Ordinal).ToArray(),
                await stateStore.LoadLogsAsync(book.Workspace, cancellationToken),
                source?.GetAssets(BookAssetKind.Interior).Count ?? 0,
                await DiscoverSourceFoldersAsync(book.Directory, cancellationToken),
                coverCandidates,
                state.SelectedCoverReference,
                state.UpdatedAt == DateTimeOffset.MinValue ? null : state.UpdatedAt,
                sourcePages,
                assetSummaries,
                fullBookChecks,
                await DescribeOutputsAsync(book.Id, state.PublishedArtifactReferences ?? [], cancellationToken),
                FindRepresentativeCoverReference(book, source, state.SelectedCoverReference),
                FolderSizeBytes: folderSizeBytes,
                HasBackground: state.HasBackground,
                ActiveInteriorSourcePageCount: activeInteriorSourcePageCount));
        }

        return new ApplicationSnapshot(discoverySnapshot, settings, summaries, DateTimeOffset.UtcNow);
    }

    private static string? FindRepresentativeCoverReference(DiscoveredBook book, BookSource? source, string? selectedCoverReference)
    {
        var covers = source?.GetAssets(BookAssetKind.Cover) ?? [];
        return covers
            .OrderByDescending(asset => string.Equals(asset.Reference, selectedCoverReference, StringComparison.OrdinalIgnoreCase))
            .ThenBy(asset => IsBookCoverAsset(book, asset) ? 0 : 1)
            .ThenBy(asset => asset.Reference, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Reference;
    }

    private static bool IsBookCoverAsset(DiscoveredBook book, BookAsset asset) =>
        Path.GetRelativePath(book.Directory.Value, asset.Reference)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .StartsWith($"Book cover{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private async ValueTask<IReadOnlyList<BookAssetSummary>> DescribeAssetsAsync(DiscoveredBook book, BookSource? source, BookProcessingState state, CancellationToken cancellationToken)
    {
        if (source is null) return [];
        var summaries = new List<BookAssetSummary>(source.Assets.Count);
        foreach (var asset in source.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileReference(asset.Reference);
            var relativePath = Path.GetRelativePath(book.Directory.Value, asset.Reference);
            ImageInfo? info = null;
            try
            {
                if (imageInspector is not null)
                {
                    using var inspection = diagnostics.Begin("image.inspect", $"{book.Id.Value}/{relativePath.Replace('\\', '/')}");
                    info = await imageInspector.GetInfoAsync(file, cancellationToken);
                }
            }
            catch (Exception) when (asset.Kind != BookAssetKind.Interior) { }
            catch (Exception) { }
            var folder = Path.GetDirectoryName(relativePath) ?? string.Empty;
            var sourceKey = asset.Kind == BookAssetKind.Interior ? InteriorSourceKey.FromBookRoot(book.Directory, file) : null;
            summaries.Add(new BookAssetSummary(
                asset.Reference,
                relativePath,
                Path.GetFileName(asset.Reference),
                folder,
                asset.Kind.ToString(),
                info?.Size.Width,
                info?.Size.Height,
                sourceKey is null ? FrameMode.Auto : state.GetInteriorFrameMode(sourceKey),
                ToLocalImageUrl(asset.Reference),
                sourceKey is null || state.IsInteriorActive(sourceKey)));
        }
        return summaries;
    }

    private static string ToLocalImageUrl(string sourceReference) =>
        new Uri(Path.GetFullPath(sourceReference)).AbsoluteUri;

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
