using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Abstractions;

/// <summary>
/// Persists an interior ordering without altering original source files.
/// </summary>
public interface IInteriorShuffleStore
{
    ValueTask<InteriorShuffleMap?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(BookWorkspace workspace, InteriorShuffleMap shuffleMap, CancellationToken cancellationToken = default);
}
