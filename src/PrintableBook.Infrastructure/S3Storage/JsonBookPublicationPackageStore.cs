using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Infrastructure.S3Storage;

public sealed class JsonBookPublicationPackageStore(IFileSystem fileSystem) : IBookPublicationPackageStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async ValueTask<BookPublicationPackage> RefreshAsync(
        string bookId,
        DirectoryReference bookDirectory,
        DirectoryReference outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new List<BookPublicationArtifactReadiness>();
        foreach (var artifact in BookOutputArtifactContract.ForBook(bookId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileReference(Path.Combine(outputDirectory.Value, artifact.FileName));
            var metadata = await fileSystem.GetFileMetadataAsync(file, cancellationToken);
            artifacts.Add(new(artifact.Index, artifact.FileName, metadata is not null, metadata?.LengthBytes, artifact.RecoveryAction));
        }

        var package = new BookPublicationPackage(1, bookId, artifacts, DateTimeOffset.UtcNow);
        await fileSystem.WriteTextAtomicallyAsync(FileFor(bookDirectory), JsonSerializer.Serialize(package, Options), cancellationToken);
        return package;
    }

    public async ValueTask<BookPublicationPackage?> LoadAsync(DirectoryReference bookDirectory, CancellationToken cancellationToken = default)
    {
        var file = FileFor(bookDirectory);
        if (!await fileSystem.FileExistsAsync(file, cancellationToken)) return null;
        try
        {
            var package = JsonSerializer.Deserialize<BookPublicationPackage>(await fileSystem.ReadTextAsync(file, cancellationToken), Options);
            return package?.Version == 1 ? package : null;
        }
        catch (JsonException) { return null; }
    }

    private static FileReference FileFor(DirectoryReference directory) =>
        new(Path.Combine(directory.Value, ".workspace", "s3-publication-package.json"));
}
