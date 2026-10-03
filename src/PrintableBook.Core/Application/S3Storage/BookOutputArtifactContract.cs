namespace PrintableBook.Core.Application.S3Storage;

public sealed record BookOutputArtifact(int Index, string FileName, string ContentType);

public static class BookOutputArtifactContract
{
    public static IReadOnlyList<BookOutputArtifact> ForBook(string bookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return
        [
            new(0, $"{bookId} - Cover.pdf", "application/pdf"),
            new(1, $"{bookId} - Interior.pdf", "application/pdf"),
            new(2, $"{bookId} - Cover_thumbnail.pdf", "application/pdf"),
            new(3, $"{bookId} - Cover_thumbnail.png", "image/png"),
            new(4, $"{bookId} - Interior_thumbnail.pdf", "application/pdf"),
            new(5, "back_cover.jpg", "image/jpeg"),
            new(6, "front_cover.jpg", "image/jpeg")
        ];
    }
}
