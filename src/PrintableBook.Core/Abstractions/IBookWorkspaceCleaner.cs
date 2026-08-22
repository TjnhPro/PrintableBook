namespace PrintableBook.Core.Abstractions;

/// <summary>
/// Removes only disposable workspace artifacts after a successful publication.
/// </summary>
public interface IBookWorkspaceCleaner
{
    ValueTask CleanAfterSuccessfulPublicationAsync(BookWorkspace workspace, CancellationToken cancellationToken = default);
}
