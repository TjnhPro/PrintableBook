using PrintableBook.Core.Abstractions;

namespace PrintableBook.Infrastructure.Workspaces;

public sealed class DisposableBookWorkspaceCleaner(IFileSystem fileSystem) : IBookWorkspaceCleaner
{
    public async ValueTask CleanAfterSuccessfulPublicationAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        await fileSystem.DeleteDirectoryAsync(
            new DirectoryReference(Path.Combine(workspace.WorkingDirectory.Value, "cache")),
            recursive: true,
            cancellationToken);
        await fileSystem.DeleteDirectoryAsync(workspace.TemporaryOutputDirectory, recursive: true, cancellationToken);
    }
}
