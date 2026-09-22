using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Application.Brands;

public sealed class BookBrandAssignmentTests
{
    [Theory]
    [InlineData("Jane Doe", " jane doe ")]
    [InlineData("JANE DOE", "Jane Doe")]
    public void Author_match_ignores_case_and_outer_whitespace(string bookAuthor, string brandAuthor) =>
        Assert.True(AuthorMatchPolicy.IsMatch(bookAuthor, brandAuthor));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    [InlineData("Jane  Doe", "Jane Doe")]
    [InlineData("J. Doe", "Jane Doe")]
    public void Author_match_rejects_missing_or_non_equivalent_values(string? bookAuthor, string? brandAuthor) =>
        Assert.False(AuthorMatchPolicy.IsMatch(bookAuthor, brandAuthor));

    [Fact]
    public void Book_metadata_accepts_descriptive_subcover_under_one_hundred_characters()
    {
        var subcover = new string('a', 99);
        var metadata = BookProductionMetadata.Create(" Title ", null, subcover, " Description ", " Author ");

        Assert.Equal("Title", metadata.Title);
        Assert.Equal(subcover.Trim(), metadata.Subcover);
        Assert.Equal("Description", metadata.Description);
        Assert.Equal("Author", metadata.Author);
    }

    [Fact]
    public void Book_metadata_rejects_subcover_at_one_hundred_characters() =>
        Assert.Throws<ArgumentException>(() => BookProductionMetadata.Create(null, null, new string('a', 100), null, null));

    [Fact]
    public void Book_metadata_allows_blank_subcover_and_partial_values()
    {
        var metadata = BookProductionMetadata.Create(null, " ", null, null, "Jane");

        Assert.Null(metadata.Title);
        Assert.Null(metadata.Subtitle);
        Assert.Null(metadata.Subcover);
        Assert.Equal("Jane", metadata.Author);
    }

    [Theory]
    [InlineData("Title\nSecond line", null, null)]
    [InlineData(null, "Subtitle\rSecond line", null)]
    [InlineData(null, null, "Author\nSecond line")]
    public void Book_metadata_rejects_line_breaks_in_single_line_fields(string? title, string? subtitle, string? author) =>
        Assert.Throws<ArgumentException>(() => BookProductionMetadata.Create(title, subtitle, null, null, author));

    [Fact]
    public void Brand_author_rejects_line_breaks() =>
        Assert.Throws<ArgumentException>(() => BrandMetadata.Create("Jane\nDoe"));

    [Theory]
    [InlineData(null, BookBrandAssignmentStatus.Unassigned)]
    [InlineData("", BookBrandAssignmentStatus.Unassigned)]
    public void Evaluator_reports_unassigned(string? assignedBrand, BookBrandAssignmentStatus expected) =>
        Assert.Equal(expected, BookBrandAssignmentEvaluator.Evaluate(assignedBrand, "Jane", null).Status);

    [Fact]
    public void Evaluator_reports_each_invalid_assignment_state()
    {
        Assert.Equal(BookBrandAssignmentStatus.MissingBrand,
            BookBrandAssignmentEvaluator.Evaluate("Brand", "Jane", null).Status);
        Assert.Equal(BookBrandAssignmentStatus.BrandMetadataUnavailable,
            BookBrandAssignmentEvaluator.Evaluate("Brand", "Jane", new("Brand", null, false)).Status);
        Assert.Equal(BookBrandAssignmentStatus.BookAuthorMissing,
            BookBrandAssignmentEvaluator.Evaluate("Brand", null, new("Brand", BrandMetadata.Create("Jane"))).Status);
        Assert.Equal(BookBrandAssignmentStatus.BrandAuthorMissing,
            BookBrandAssignmentEvaluator.Evaluate("Brand", "Jane", new("Brand", null)).Status);
        Assert.Equal(BookBrandAssignmentStatus.AuthorMismatch,
            BookBrandAssignmentEvaluator.Evaluate("Brand", "Jane", new("Brand", BrandMetadata.Create("John"))).Status);
    }

    [Fact]
    public void Evaluator_accepts_matching_authors() =>
        Assert.Equal(
            BookBrandAssignmentStatus.Valid,
            BookBrandAssignmentEvaluator.Evaluate("Brand", " Jane Doe ", new("Brand", BrandMetadata.Create("jane doe"))).Status);
}
