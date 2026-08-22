using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record BookValidationCheck(string Code, string Message, bool IsSuccess);
public sealed record InteriorPageSummary(string PageId, string Status, string FinalPagePath);
public sealed record BookFolderSummary(string Name, string Status, int FileCount, int ImageCount);
public sealed record BookDesktopSummary(BookId BookId, string ValidationStatus, IReadOnlyList<BookValidationCheck> ValidationChecks, BookProcessingStatus WorkspaceStatus, string? CurrentStep, string? FailureMessage, IReadOnlyList<string> PublishedArtifacts, IReadOnlyList<InteriorPageSummary> InteriorPages, IReadOnlyList<BookProcessingLogEntry> Logs, int InteriorSourcePageCount, IReadOnlyList<BookFolderSummary>? SourceFolders = null, IReadOnlyList<string>? CoverCandidates = null, string? SelectedCoverReference = null, DateTimeOffset? LastRunAt = null);
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
    IFileSystem fileSystem) : IApplicationSnapshotService
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
                checks.Add(new BookValidationCheck("book.source_ready", "Cover and Interior source images were discovered.", true));
            }
            else
            {
                checks.Add(new BookValidationCheck(scan.Failure!.Code, scan.Failure.Message, false));
            }
            if (coverCandidates.Length > 1)
            {
                checks.Add(new BookValidationCheck(
                    "book.cover_selection_required",
                    hasSelectedCover ? "A cover candidate was selected." : "Select one cover candidate before processing.",
                    hasSelectedCover));
            }
            var isReady = scan.IsSuccess && hasSelectedCover;
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
                state.UpdatedAt == DateTimeOffset.MinValue ? null : state.UpdatedAt));
        }

        return new ApplicationSnapshot(discoverySnapshot, settings, summaries, DateTimeOffset.UtcNow);
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
