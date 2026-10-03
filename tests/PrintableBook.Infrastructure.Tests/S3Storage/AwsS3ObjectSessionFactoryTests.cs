using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using PrintableBook.Infrastructure.S3Storage;

namespace PrintableBook.Infrastructure.Tests.S3Storage;

public sealed class AwsS3ObjectSessionFactoryTests
{
    [Fact]
    public void Client_configuration_uses_regional_dual_stack_path_style_and_bounded_sdk_retry()
    {
        var configuration = AwsS3ObjectSessionFactory.CreateConfig("us-east-1");

        Assert.Equal("us-east-1", configuration.RegionEndpoint.SystemName);
        Assert.True(configuration.ForcePathStyle);
        Assert.True(configuration.UseDualstackEndpoint);
        Assert.Equal(3, configuration.MaxErrorRetry);
        Assert.Equal(RequestRetryMode.Standard, configuration.RetryMode);
    }

    [Fact]
    public void Empty_list_response_is_treated_as_an_empty_prefix()
    {
        var response = new ListObjectsV2Response();

        Assert.Null(response.S3Objects);
        Assert.Empty(AwsS3ObjectSessionFactory.EnumerateObjects(response));
    }

    [Theory]
    [InlineData("AccessControlListNotSupported", HttpStatusCode.BadRequest, "s3_public_acl_unsupported")]
    [InlineData("NoSuchBucket", HttpStatusCode.NotFound, "s3_bucket_not_found")]
    [InlineData("AuthorizationHeaderMalformed", HttpStatusCode.BadRequest, "s3_region_mismatch")]
    [InlineData("PermanentRedirect", HttpStatusCode.MovedPermanently, "s3_region_mismatch")]
    [InlineData("InvalidAccessKeyId", HttpStatusCode.Forbidden, "s3_credentials_invalid")]
    [InlineData("SignatureDoesNotMatch", HttpStatusCode.Forbidden, "s3_credentials_invalid")]
    [InlineData("AccessDenied", HttpStatusCode.Forbidden, "s3_access_denied")]
    [InlineData("Unexpected", HttpStatusCode.ServiceUnavailable, "s3_request_failed")]
    public void Aws_error_codes_map_to_stable_product_errors(string awsCode, HttpStatusCode status, string expected)
    {
        var exception = new AmazonS3Exception("failure") { ErrorCode = awsCode, StatusCode = status };

        Assert.Equal(expected, AwsS3ObjectSessionFactory.Map(exception).Code);
    }
}
