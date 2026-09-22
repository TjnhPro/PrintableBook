namespace PrintableBook.Core.Application.Brands;

public enum BookBrandAssignmentStatus
{
    Unassigned = 0,
    Valid = 1,
    BookAuthorMissing = 2,
    BrandAuthorMissing = 3,
    AuthorMismatch = 4,
    MissingBrand = 5,
    BrandMetadataUnavailable = 6
}

public sealed record BrandAssignmentTarget(
    string BrandName,
    BrandMetadata? Metadata,
    bool MetadataAvailable = true);

public sealed record BookBrandAssignmentEvaluation(
    BookBrandAssignmentStatus Status,
    string? Reason)
{
    public bool IsValid => Status == BookBrandAssignmentStatus.Valid;
}

public static class BookBrandAssignmentEvaluator
{
    public static BookBrandAssignmentEvaluation Evaluate(
        string? assignedBrand,
        string? bookAuthor,
        BrandAssignmentTarget? target)
    {
        if (string.IsNullOrWhiteSpace(assignedBrand))
        {
            return new(BookBrandAssignmentStatus.Unassigned, null);
        }

        if (target is null)
        {
            return new(BookBrandAssignmentStatus.MissingBrand, $"Assigned Brand '{assignedBrand}' is no longer available.");
        }

        if (!target.MetadataAvailable)
        {
            return new(BookBrandAssignmentStatus.BrandMetadataUnavailable, $"Brand metadata for '{target.BrandName}' could not be read.");
        }

        if (string.IsNullOrWhiteSpace(bookAuthor))
        {
            return new(BookBrandAssignmentStatus.BookAuthorMissing, "Save a Book Author before assigning a Brand.");
        }

        if (string.IsNullOrWhiteSpace(target.Metadata?.Author))
        {
            return new(BookBrandAssignmentStatus.BrandAuthorMissing, $"Brand '{target.BrandName}' does not have an Author.");
        }

        return AuthorMatchPolicy.IsMatch(bookAuthor, target.Metadata.Author)
            ? new(BookBrandAssignmentStatus.Valid, null)
            : new(BookBrandAssignmentStatus.AuthorMismatch, $"Book Author does not match Brand '{target.BrandName}'.");
    }
}
