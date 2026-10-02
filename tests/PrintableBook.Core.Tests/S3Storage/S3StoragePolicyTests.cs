using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Core.Tests.S3Storage;

public sealed class S3StoragePolicyTests
{
    [Theory]
    [InlineData(" b0fcc8q6jp ", "B0FCC8Q6JP")]
    [InlineData("B0FCC8Q6J", "")]
    [InlineData("B0FCC8Q6J-", "")]
    [InlineData(null, "")]
    public void NormalizeAsin_requires_ten_alphanumeric_characters(string? value, string expected)
    {
        Assert.Equal(expected, S3StoragePolicy.NormalizeAsin(value));
    }

    [Fact]
    public void FileNames_returns_the_seven_required_output_contracts()
    {
        var files = S3StoragePolicy.FileNames("Book One");

        Assert.Equal(7, files.Count);
        Assert.Equal("Book One - Cover.pdf", files[0]);
        Assert.Contains("Book One - Cover_thumbnail_back.png", files);
        Assert.Contains("Book One - Cover_thumbnail_front.png", files);
        Assert.Equal("Book One - Interior_thumbnail.pdf", files[^1]);
    }

    [Fact]
    public void ObjectKey_and_public_url_use_canonical_asin_and_encoded_file_name()
    {
        var settings = new S3StorageSettings("access", "secret", "vxgroup.tinh", "us-east-1", "https://s3.dualstack.us-east-1.amazonaws.com/");
        var key = S3StoragePolicy.ObjectKey("b0fcc8q6jp", "Book One - Cover.pdf");

        Assert.Equal("B0FCC8Q6JP/Book One - Cover.pdf", key);
        Assert.Equal(
            "https://s3.dualstack.us-east-1.amazonaws.com/vxgroup.tinh/B0FCC8Q6JP/Book%20One%20-%20Cover.pdf",
            S3StoragePolicy.PublicUrl(settings, key));
    }

    [Fact]
    public void Validate_rejects_non_http_public_url()
    {
        var exception = Assert.Throws<S3StorageValidationException>(() =>
            S3StoragePolicy.Validate(new("access", "secret", "valid-bucket", "us-east-1", "file:///tmp/storage")));

        Assert.Equal("s3_url_invalid", exception.Code);
    }
}
