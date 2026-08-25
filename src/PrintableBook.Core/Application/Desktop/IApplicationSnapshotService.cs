using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record BookValidationCheck(string Code, string Message, bool IsSuccess, bool IsWarning = false);
public sealed record InteriorPageSummary(string PageId, string Status, string FinalPagePath);
public sealed record InteriorSourcePageSummary(string SourceReference, FrameMode FrameMode);
public sealed record BookFolderSummary(string Name, string Status, int FileCount, int ImageCount);
public sealed record BookAssetSummary(string SourceReference, string RelativePath, string FileName, string Folder, string Kind, int? Width, int? Height, FrameMode FrameMode, bool PreviewAvailable);
public sealed record BookAssetPreview(string SourceReference, int Width, int Height, string DataUrl);
public sealed record BookOutputSummary(string ArtifactReference, string FileName, long FileSizeBytes, int? PageCount, double? WidthInches, double? HeightInches, string VerificationStatus, DateTimeOffset? GeneratedAt);
public interface ILocalOutputActionService
{
    ValueTask OpenAsync(FileReference file, CancellationToken cancellationToken = default);
    ValueTask RevealAsync(FileReference file, CancellationToken cancellationToken = default);
    ValueTask CopyPathAsync(FileReference file, CancellationToken cancellationToken = default);
}
public interface IBookAssetPreviewService
{
    ValueTask<BookAssetPreview?> GetAsync(string bookId, string sourceReference, CancellationToken cancellationToken = default);
}
public sealed record BookDesktopSummary(BookId BookId, string ValidationStatus, IReadOnlyList<BookValidationCheck> ValidationChecks, BookProcessingStatus WorkspaceStatus, string? CurrentStep, string? FailureMessage, IReadOnlyList<string> PublishedArtifacts, IReadOnlyList<InteriorPageSummary> InteriorPages, IReadOnlyList<BookProcessingLogEntry> Logs, int InteriorSourcePageCount, IReadOnlyList<BookFolderSummary>? SourceFolders = null, IReadOnlyList<string>? CoverCandidates = null, string? SelectedCoverReference = null, DateTimeOffset? LastRunAt = null, IReadOnlyList<InteriorSourcePageSummary>? InteriorSourcePages = null, IReadOnlyList<BookAssetSummary>? Assets = null, IReadOnlyList<BookValidationCheck>? FullBookValidationChecks = null, IReadOnlyList<BookOutputSummary>? OutputSummaries = null);
public sealed record ApplicationSnapshot(ApplicationDiscovery Discovery, GlobalSettings GlobalSettings, IReadOnlyList<BookDesktopSummary> BookSummaries, DateTimeOffset RefreshedAt);

public interface IApplicationSnapshotService
{
    ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationSnapshotService(
    IApplicationRootDiscovery discovery,
    IGlobalSettingsStore settingsStore,
    IBookSourceScanner sourceScanner,
    IBookWorkspaceStateStore stateStore,
    IFileSystem fileSystem,
    IImageInspector? imageInspector = null,
    IPdfDocumentInspector? pdfDocumentInspector = null) : IApplicationSnapshotService
{
    public async ValueTask<ApplicationSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var discoverySnapshot = await discovery.DiscoverAsync(cancellationToken);
        var settings = await settingsStore.LoadAsync(discoverySnapshot.Paths, cancellationToken);
        var summaries = new List<BookDesktopSummary>(discoverySnapshot.Books.Count);
        foreach (var book in discoverySnapshot.Books)
        {
            var scan = await sourceScanner.ScanAsync(book.Id, book.Directory, cancellationToken);
            var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
            var coverCandidates = scan.Source?.GetAssets(BookAssetKind.Cover).Select(asset => asset.Reference).ToArray() ?? [];
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
            if (scan.IsSuccess)
            {
                checks.Add(new BookValidationCheck("book.interior_ready", "Interior source images were discovered.", true));
            }
            else
            {
                checks.Add(new BookValidationCheck(scan.Failure!.Code, scan.Failure.Message, false));
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
                scan.IsSuccess
                    ? new BookValidationCheck("book.interior_ready", "Interior source images were discovered.", true)
                    : new BookValidationCheck(scan.Failure!.Code, scan.Failure.Message, false)
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
            var isReady = scan.IsSuccess;
            var sourcePages = scan.Source?.GetAssets(BookAssetKind.Interior)
                .Select(asset => new InteriorSourcePageSummary(asset.Reference, state.GetInteriorFrameMode(InteriorSourceKey.FromBookRoot(book.Directory, new FileReference(asset.Reference)))))
                .ToArray() ?? [];
            var assetSummaries = await DescribeAssetsAsync(book, scan.Source, state, cancellationToken);
            summaries.Add(new BookDesktopSummary(
                book.Id,
                isReady ? "Ready" : scan.IsSuccess ? "Needs selection" : "Invalid",
                checks,
                state.Status,
                state.CurrentStep,
                state.Failure?.Message,
                state.PublishedArtifactReferences ?? [],
                interiorPages.OrderBy(page => page.PageId, StringComparer.Ordinal).ToArray(),
                await stateStore.LoadLogsAsync(book.Workspace, cancellationToken),
                scan.Source?.GetAssets(BookAssetKind.Interior).Count ?? 0,
                await DiscoverSourceFoldersAsync(book.Directory, cancellationToken),
                coverCandidates,
                state.SelectedCoverReference,
                state.UpdatedAt == DateTimeOffset.MinValue ? null : state.UpdatedAt,
                sourcePages,
                assetSummaries,
                fullBookChecks,
                await DescribeOutputsAsync(state.PublishedArtifactReferences ?? [], cancellationToken)));
        }

        return new ApplicationSnapshot(discoverySnapshot, settings, summaries, DateTimeOffset.UtcNow);
    }

    private async ValueTask<IReadOnlyList<BookAssetSummary>> DescribeAssetsAsync(DiscoveredBook book, BookSource? source, BookProcessingState state, CancellationToken cancellationToken)
    {
        if (source is null) return [];
        var summaries = new List<BookAssetSummary>(source.Assets.Count);
        foreach (var asset in source.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileReference(asset.Reference);
            ImageInfo? info = null;
            try { info = imageInspector is null ? null : await imageInspector.GetInfoAsync(file, cancellationToken); }
            catch (Exception) when (asset.Kind != BookAssetKind.Interior) { }
            catch (Exception) { }
            var relativePath = Path.GetRelativePath(book.Directory.Value, asset.Reference);
            var folder = Path.GetDirectoryName(relativePath) ?? string.Empty;
            summaries.Add(new BookAssetSummary(
                asset.Reference,
                relativePath,
                Path.GetFileName(asset.Reference),
                folder,
                asset.Kind.ToString(),
                info?.Size.Width,
                info?.Size.Height,
                state.GetInteriorFrameMode(InteriorSourceKey.FromBookRoot(book.Directory, file)),
                info is not null));
        }
        return summaries;
    }

    private async ValueTask<IReadOnlyList<BookOutputSummary>> DescribeOutputsAsync(IReadOnlyList<string> artifacts, CancellationToken cancellationToken)
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
                var inspection = pdfDocumentInspector is null ? null : await pdfDocumentInspector.InspectAsync(new FileReference(artifact), cancellationToken);
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
