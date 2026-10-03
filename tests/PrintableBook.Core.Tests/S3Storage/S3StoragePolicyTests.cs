using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Core.Tests.S3Storage;

public sealed class S3StoragePolicyTests
{
    [Theory]
    [InlineData(" b0fcc8q6jp ", "B0FCC8Q6JP")]
    [InlineData("B0FCC8Q6J", "")]
    [InlineData("B0FCC8Q6J-", "")]
    [InlineData(null, "")]
    public void NormalizeAsin_requires_ten_alphanumeric_characters(string? value, string expected) =>
        Assert.Equal(expected, S3StoragePolicy.NormalizeAsin(value));

    [Fact]
    public void FileNames_returns_the_seven_required_output_contracts_in_stable_order()
    {
        Assert.Equal(
            [
                "Book One - Cover.pdf",
                "Book One - Interior.pdf",
                "Book One - Cover_thumbnail.pdf",
                "Book One - Cover_thumbnail.png",
                "Book One - Interior_thumbnail.pdf",
                "back_cover.jpg",
                "front_cover.jpg"
            ],
            S3StoragePolicy.FileNames("Book One"));
    }

    [Fact]
    public void ObjectKey_and_public_url_use_folder_canonical_asin_and_encoded_file_name()
    {
        var settings = new S3StorageSettings("access", "secret", new("us-east-1", "vxgroup.tinh", "coloring"));
        var key = S3StoragePolicy.ObjectKey("coloring", "b0fcc8q6jp", "Book One - Cover.pdf");

        Assert.Equal("coloring/B0FCC8Q6JP/Book One - Cover.pdf", key);
        Assert.Equal(
            "https://s3.dualstack.us-east-1.amazonaws.com/vxgroup.tinh/coloring/B0FCC8Q6JP/Book%20One%20-%20Cover.pdf",
            S3StoragePolicy.PublicUrl(settings, key));
    }

    [Fact]
    public void Validate_rejects_parent_folder_segments()
    {
        var exception = Assert.Throws<S3StorageValidationException>(() =>
            S3StoragePolicy.Validate(new("access", "secret", new("us-east-1", "valid-bucket", "coloring/../private"))));

        Assert.Equal("s3_folder_invalid", exception.Code);
    }
}
