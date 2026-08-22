using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record BookValidationCheck(string Code, string Message, bool IsSuccess);
public sealed record InteriorPageSummary(string PageId, string Status, string FinalPagePath);
public sealed record BookDesktopSummary(BookId BookId, string ValidationStatus, IReadOnlyList<BookValidationCheck> ValidationChecks, BookProcessingStatus WorkspaceStatus, string? CurrentStep, string? FailureMessage, IReadOnlyList<string> PublishedArtifacts, IReadOnlyList<InteriorPageSummary> InteriorPages, IReadOnlyList<BookProcessingLogEntry> Logs);
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
            var interiorDirectory = new DirectoryReference(Path.Combine(book.Workspace.ProcessedDirectory.Value, "interior"));
            var interiorPages = new List<InteriorPageSummary>();
            await foreach (var page in fileSystem.EnumerateFilesAsync(interiorDirectory, cancellationToken))
            {
                if (string.Equals(Path.GetExtension(page.Value), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    interiorPages.Add(new InteriorPageSummary(Path.GetFileNameWithoutExtension(page.Value), "Completed", page.Value));
                }
            }
            IReadOnlyList<BookValidationCheck> checks = scan.IsSuccess
                ? [new BookValidationCheck("book.source_ready", "Cover and Interior source files were discovered.", true)]
                : [new BookValidationCheck(scan.Failure!.Code, scan.Failure.Message, false)];
            summaries.Add(new BookDesktopSummary(
                book.Id,
                scan.IsSuccess ? "Ready" : "Invalid",
                checks,
                state.Status,
                state.CurrentStep,
                state.Failure?.Message,
                state.PublishedArtifactReferences ?? [],
                interiorPages.OrderBy(page => page.PageId, StringComparer.Ordinal).ToArray(),
                await stateStore.LoadLogsAsync(book.Workspace, cancellationToken)));
        }

        return new ApplicationSnapshot(discoverySnapshot, settings, summaries, DateTimeOffset.UtcNow);
    }
}
