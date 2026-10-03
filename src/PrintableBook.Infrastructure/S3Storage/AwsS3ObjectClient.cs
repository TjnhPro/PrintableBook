using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Infrastructure.S3Storage;

public sealed class AwsS3ObjectSessionFactory(IHttpClientFactory httpClientFactory) : IS3ObjectSessionFactory
{
    public ValueTask<IS3ObjectSession> OpenAsync(S3StorageSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = CreateConfig(settings.Region);
        IS3ObjectSession session = new Session(new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config), httpClientFactory.CreateClient("S3PublicVerification"), settings);
        return ValueTask.FromResult(session);
    }

    internal static AmazonS3Config CreateConfig(string region) => new()
    {
        RegionEndpoint = RegionEndpoint.GetBySystemName(region),
        ForcePathStyle = true,
        UseDualstackEndpoint = true,
        MaxErrorRetry = 3,
        RetryMode = RequestRetryMode.Standard
    };

    private sealed class Session(AmazonS3Client client, HttpClient publicClient, S3StorageSettings settings) : IS3ObjectSession
    {
        public async ValueTask<S3RemotePrefix> ListPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            string? continuationToken = null;
            try
            {
                do
                {
                    var response = await client.ListObjectsV2Async(new ListObjectsV2Request
                    {
                        BucketName = settings.Bucket,
                        Prefix = prefix,
                        ContinuationToken = continuationToken
                    }, cancellationToken);
                    foreach (var item in response.S3Objects) keys.Add(item.Key);
                    continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
                }
                while (!string.IsNullOrEmpty(continuationToken));
                return new(keys);
            }
            catch (AmazonS3Exception exception) { throw Map(exception); }
            catch (AmazonServiceException exception) { throw new S3StorageRemoteException("s3_service_unavailable", "S3 could not be reached.", exception); }
        }

        public async ValueTask<S3RemoteObject?> HeadAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = settings.Bucket, Key = objectKey }, cancellationToken);
                var hash = ReadMetadata(response.Metadata, S3StoragePolicy.Sha256MetadataName) ?? string.Empty;
                var metadataLengthText = ReadMetadata(response.Metadata, S3StoragePolicy.LengthMetadataName);
                long? metadataLength = long.TryParse(metadataLengthText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedLength)
                    ? parsedLength
                    : null;
                var isPublic = await IsPublicAsync(S3StoragePolicy.PublicUrl(settings, objectKey), response.ContentLength, cancellationToken);
                return new(hash, response.ContentLength, isPublic, metadataLength);
            }
            catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound || exception.ErrorCode is "NoSuchKey" or "NotFound") { return null; }
            catch (AmazonS3Exception exception) { throw Map(exception); }
            catch (AmazonServiceException exception) { throw new S3StorageRemoteException("s3_service_unavailable", "S3 could not be reached.", exception); }
        }

        public async ValueTask PutAsync(string objectKey, FileReference source, string contentType, string sha256, long length, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new PutObjectRequest
                {
                    BucketName = settings.Bucket,
                    Key = objectKey,
                    FilePath = source.Value,
                    CannedACL = S3CannedACL.PublicRead,
                    ContentType = contentType
                };
                request.Metadata[S3StoragePolicy.Sha256MetadataName] = sha256;
                request.Metadata[S3StoragePolicy.LengthMetadataName] = length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await client.PutObjectAsync(request, cancellationToken);
            }
            catch (AmazonS3Exception exception) { throw Map(exception); }
            catch (AmazonServiceException exception) { throw new S3StorageRemoteException("s3_service_unavailable", "S3 could not be reached.", exception); }
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }

        private async ValueTask<bool> IsPublicAsync(string url, long expectedLength, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await publicClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                return response.IsSuccessStatusCode && (!response.Content.Headers.ContentLength.HasValue || response.Content.Headers.ContentLength.Value == expectedLength);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
            catch (HttpRequestException) { return false; }
        }

        private static string? ReadMetadata(MetadataCollection metadata, string name)
        {
            var key = metadata.Keys.FirstOrDefault(candidate =>
                string.Equals(candidate, $"x-amz-meta-{name}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
            return key is null ? null : metadata[key];
        }
    }

    internal static S3StorageRemoteException Map(AmazonS3Exception exception) => exception.ErrorCode switch
    {
        "AccessControlListNotSupported" => new("s3_public_acl_unsupported", "The bucket does not allow public-read ACLs. Enable ACLs or choose a compatible bucket.", exception),
        "NoSuchBucket" => new("s3_bucket_not_found", "The configured S3 bucket does not exist.", exception),
        "AuthorizationHeaderMalformed" or "PermanentRedirect" => new("s3_region_mismatch", "The bucket is in a different AWS region.", exception),
        "InvalidAccessKeyId" or "SignatureDoesNotMatch" => new("s3_credentials_invalid", "S3 credentials were rejected.", exception),
        "AccessDenied" => new("s3_access_denied", "S3 denied access. Check IAM permissions, bucket policy, and public-read ACL settings.", exception),
        _ when exception.StatusCode == HttpStatusCode.Forbidden => new("s3_access_denied", "S3 denied access. Check IAM permissions and bucket policy.", exception),
        _ => new("s3_request_failed", "The S3 request failed.", exception)
    };
}
