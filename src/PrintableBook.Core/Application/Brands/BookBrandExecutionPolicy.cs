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
        if (string.IsNullOrWhiteSpace(assignedBrand)) return BookBrandExecutionDecision.Allowed;

        if (assignmentStatus != BookBrandAssignmentStatus.Valid)
        {
            return new(false, "book_brand_assignment_invalid", $"Assigned Brand '{assignedBrand}' is invalid. Reassign or unassign the Book before continuing.");
        }

        if (!string.Equals(assignedBrand, requestedBrand, StringComparison.Ordinal))
        {
            return new(false, "book_brand_mismatch", $"Select Processing Brand '{assignedBrand}' to continue.");
        }

        return BookBrandExecutionDecision.Allowed;
    }
}
