using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.S3Storage;

public sealed record BookPublicationArtifactReadiness(
    int Index,
    string FileName,
    bool Exists,
    long? Length,
    string RecoveryAction);

public sealed record BookPublicationPackage(
    int Version,
    string BookId,
    IReadOnlyList<BookPublicationArtifactReadiness> Artifacts,
    DateTimeOffset RefreshedAtUtc)
{
    public bool IsComplete => Artifacts.Count == 7 && Artifacts.All(artifact => artifact.Exists);
}

public interface IBookPublicationPackageStore
{
    ValueTask<BookPublicationPackage> RefreshAsync(
        string bookId,
        DirectoryReference bookDirectory,
        DirectoryReference outputDirectory,
        CancellationToken cancellationToken = default);

    ValueTask<BookPublicationPackage?> LoadAsync(
        DirectoryReference bookDirectory,
        CancellationToken cancellationToken = default);
}
