namespace PrintableBook.Core.Application.Brands;

public sealed record BookBrandExecutionDecision(bool IsAllowed, string? Code = null, string? Message = null)
{
    public static BookBrandExecutionDecision Allowed { get; } = new(true);
}

public static class BookBrandExecutionPolicy
{
    public static BookBrandExecutionDecision Evaluate(
        string? assignedBrand,
        BookBrandAssignmentStatus assignmentStatus,
        string? requestedBrand)
    {
        if (string.IsNullOrWhiteSpace(assignedBrand))
        {
            return new(false, "book_brand_assignment_required", "Assign a Brand to this Book before continuing.");
        }

        if (assignmentStatus != BookBrandAssignmentStatus.Valid)
        {
            return new(false, "book_brand_assignment_invalid", $"Assigned Brand '{assignedBrand}' is invalid. Reassign or unassign the Book before continuing.");
        }

        if (!string.IsNullOrWhiteSpace(requestedBrand) &&
            !string.Equals(assignedBrand, requestedBrand, StringComparison.Ordinal))
        {
            return new(false, "book_brand_mismatch", $"Requested Brand does not match the Book's assigned Brand '{assignedBrand}'.");
        }

        return BookBrandExecutionDecision.Allowed;
    }
}
