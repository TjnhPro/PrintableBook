using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Abstractions;

/// <summary>
/// Infrastructure persistence boundary for a book's workspace state and diagnostics.
/// </summary>
public interface IBookWorkspaceStateStore
{
    ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default);

    ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default);

    ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default);
}
