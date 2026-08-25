using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Processing;

public sealed record BookProcessingProgress
{
    public BookProcessingProgress(
        BookId bookId,
        BookProcessingStatus status,
        string step,
        int? pagesCompleted = null,
        int? pagesTotal = null,
        string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(step)) throw new ArgumentException("A processing step is required.", nameof(step));
        if (pagesCompleted is < 0) throw new ArgumentOutOfRangeException(nameof(pagesCompleted));
        if (pagesTotal is <= 0) throw new ArgumentOutOfRangeException(nameof(pagesTotal));
        if (pagesCompleted is not null && pagesTotal is not null && pagesCompleted > pagesTotal) throw new ArgumentOutOfRangeException(nameof(pagesCompleted));

        BookId = bookId;
        Status = status;
        Step = step;
        PagesCompleted = pagesCompleted;
        PagesTotal = pagesTotal;
        Detail = detail;
    }

    public BookId BookId { get; }
    public BookProcessingStatus Status { get; }
    public string Step { get; }
    public int? PagesCompleted { get; }
    public int? PagesTotal { get; }
    public string? Detail { get; }
}
