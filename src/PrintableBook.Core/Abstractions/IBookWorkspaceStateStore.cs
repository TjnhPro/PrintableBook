using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Abstractions;

public sealed record BookWorkspaceStateLoadResult(
    BookProcessingState? State,
    int SourceFrameModeContractVersion,
    bool LegacyFrameContractDetected,
    IReadOnlyList<string>? ExplicitLegacyAutoSourceKeys = null,
    IReadOnlyList<string>? ExplicitLegacyFrameSourceKeys = null);

/// <summary>
/// Infrastructure persistence boundary for a book's workspace state and diagnostics.
/// </summary>
public interface IBookWorkspaceStateStore
{
    ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default);

    async ValueTask<BookWorkspaceStateLoadResult> LoadWithMetadataAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) =>
        new(
            await LoadAsync(workspace, cancellationToken),
            BookProcessingState.CurrentFrameModeContractVersion,
            LegacyFrameContractDetected: false);

    ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default);

    ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default);

    ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default);
}
