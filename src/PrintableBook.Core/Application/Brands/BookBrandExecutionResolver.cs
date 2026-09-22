using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.Brands;

public sealed record ResolvedBookBrandContext(
    DiscoveredBook Book,
    BookDesktopSummary Summary,
    DiscoveredBrand Brand);

public sealed record BookBrandExecutionFailure(
    string Code,
    string Message,
    BookId? BookId = null);

public sealed record BookBrandBatchResolution(
    IReadOnlyList<ResolvedBookBrandContext> Books,
    DiscoveredBrand? Brand,
    BookBrandExecutionFailure? Failure)
{
    public bool IsSuccess => Failure is null && Brand is not null;
}

public static class BookBrandExecutionResolver
{
    public static BookBrandBatchResolution ResolveBatch(
        ApplicationSnapshot snapshot,
        IReadOnlyList<string> orderedBookIds,
        string? requestedBrand = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(orderedBookIds);

        var contexts = new List<ResolvedBookBrandContext>(orderedBookIds.Count);
        foreach (var requestedBookId in orderedBookIds)
        {
            var book = snapshot.Discovery.Books.FirstOrDefault(candidate =>
                string.Equals(candidate.Id.Value, requestedBookId, StringComparison.Ordinal));
            if (book is null)
            {
                return Failed("book_not_found", $"Book '{requestedBookId}' is no longer available.", new BookId(requestedBookId));
            }

            var summary = snapshot.BookSummaries.FirstOrDefault(candidate => candidate.BookId == book.Id);
            if (summary is null)
            {
                return Failed("book_not_found", $"Book '{requestedBookId}' does not have a current catalog summary.", book.Id);
            }

            var execution = BookBrandExecutionPolicy.Evaluate(summary.AssignedBrand, summary.AssignmentStatus, null);
            if (!execution.IsAllowed)
            {
                var message = summary.AssignmentStatus == BookBrandAssignmentStatus.Unassigned
                    ? execution.Message!
                    : summary.AssignmentReason ?? execution.Message!;
                return Failed(execution.Code!, message, book.Id);
            }

            var brand = snapshot.Discovery.Brands.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, summary.AssignedBrand, StringComparison.Ordinal));
            if (brand is null)
            {
                return Failed(
                    "book_brand_assignment_invalid",
                    $"Assigned Brand '{summary.AssignedBrand}' is no longer available. Reassign the Book before continuing.",
                    book.Id);
            }

            contexts.Add(new ResolvedBookBrandContext(book, summary, brand));
        }

        var resolvedBrands = contexts
            .Select(context => context.Brand.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (resolvedBrands.Length > 1)
        {
            return Failed(
                "mixed_assigned_brands_not_supported",
                "Selected Books belong to different Brands. Filter and process one Brand at a time.");
        }

        var resolvedBrand = contexts.FirstOrDefault()?.Brand;
        if (resolvedBrand is null)
        {
            return Failed("book_not_found", "Select at least one Book before continuing.");
        }

        if (!string.IsNullOrWhiteSpace(requestedBrand) &&
            !string.Equals(resolvedBrand.Name, requestedBrand, StringComparison.Ordinal))
        {
            return Failed(
                "book_brand_mismatch",
                $"Requested Brand does not match the assigned Brand '{resolvedBrand.Name}'.");
        }

        return new BookBrandBatchResolution(contexts, resolvedBrand, null);
    }

    private static BookBrandBatchResolution Failed(string code, string message, BookId? bookId = null) =>
        new([], null, new BookBrandExecutionFailure(code, message, bookId));
}
