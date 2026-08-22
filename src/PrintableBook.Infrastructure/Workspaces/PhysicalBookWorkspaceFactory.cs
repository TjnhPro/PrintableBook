using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Infrastructure.Workspaces;

public sealed class PhysicalBookWorkspaceFactory(IFileSystem fileSystem) : IBookWorkspaceFactory
{
    public async ValueTask<BookWorkspace> CreateAsync(
        BookId bookId,
        DirectoryReference bookDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookId);
        ArgumentNullException.ThrowIfNull(bookDirectory);

        var workspaceDirectory = new DirectoryReference(Path.Combine(bookDirectory.Value, ".workspace"));
        var processedDirectory = new DirectoryReference(Path.Combine(workspaceDirectory.Value, "processed"));
        var temporaryOutputDirectory = new DirectoryReference(Path.Combine(workspaceDirectory.Value, "output-temp"));
        foreach (var name in new[] { "state", "logs", "errors", "cache", "processed", "processed/interior", "output-temp" })
        {
            await fileSystem.CreateDirectoryAsync(new DirectoryReference(Path.Combine(workspaceDirectory.Value, name)), cancellationToken);
        }

        return new BookWorkspace(bookId, workspaceDirectory, processedDirectory, temporaryOutputDirectory);
    }
}
