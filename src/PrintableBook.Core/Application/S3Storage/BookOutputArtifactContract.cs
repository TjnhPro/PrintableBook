namespace PrintableBook.Core.Application.S3Storage;

public sealed record BookOutputArtifact(int Index, string FileName, string ContentType, string RecoveryAction);

public static class BookOutputArtifactContract
{
    public static IReadOnlyList<BookOutputArtifact> ForBook(string bookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return
        [
            new(0, $"{bookId} - Cover.pdf", "application/pdf", "Build Cover PDF"),
            new(1, $"{bookId} - Interior.pdf", "application/pdf", "Build Final Interior"),
            new(2, $"{bookId} - Cover_thumbnail.pdf", "application/pdf", "Build Cover PDF"),
            new(3, $"{bookId} - Cover_thumbnail.png", "image/png", "Build Cover PDF"),
            new(4, $"{bookId} - Interior_thumbnail.pdf", "application/pdf", "Build Final Interior"),
            new(5, "back_cover.jpg", "image/jpeg", "Build Cover PDF"),
            new(6, "front_cover.jpg", "image/jpeg", "Build Cover PDF")
        ];
    }
}
