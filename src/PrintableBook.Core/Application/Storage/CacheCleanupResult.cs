using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.Storage;

public sealed record CacheCleanupResult(
    int ScannedBooks,
    int CleanedBooks,
    int SkippedBooks,
    int FailedBooks,
    long FreedBytes,
    IReadOnlyList<CacheCleanupBookResult> Books);

public sealed record CacheCleanupBookResult(
    BookId BookId,
    string Status,
    long FreedBytes,
    string? Reason);
