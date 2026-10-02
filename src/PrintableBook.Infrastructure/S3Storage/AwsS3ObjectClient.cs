using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Infrastructure.S3Storage;

public sealed class AwsS3ObjectClient : IS3ObjectClient
{
    public async ValueTask<S3RemoteObject?> GetAsync(S3StorageSettings settings, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(settings);
            var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = settings.Bucket,
                Key = objectKey
            }, cancellationToken);
            var prefixedKey = $"x-amz-meta-{S3StoragePolicy.Sha256MetadataName}";
            var metadataKey = response.Metadata.Keys.FirstOrDefault(key => string.Equals(key, prefixedKey, StringComparison.OrdinalIgnoreCase))
                ?? response.Metadata.Keys.FirstOrDefault(key => string.Equals(key, S3StoragePolicy.Sha256MetadataName, StringComparison.OrdinalIgnoreCase));
            var hash = metadataKey is null ? string.Empty : response.Metadata[metadataKey];
            return new(hash ?? string.Empty);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound || exception.ErrorCode is "NoSuchKey" or "NotFound")
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            throw Map(exception);
        }
        catch (AmazonServiceException exception)
        {
            throw new S3StorageRemoteException("s3_service_unavailable", "S3 could not be reached.", exception);
        }
    }

    public async ValueTask UploadAsync(S3StorageSettings settings, string objectKey, FileReference source, string sha256, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(settings);
            var request = new PutObjectRequest
            {
                BucketName = settings.Bucket,
                Key = objectKey,
                FilePath = source.Value,
                CannedACL = S3CannedACL.PublicRead,
                ContentType = ContentType(source.Value)
            };
            request.Metadata[S3StoragePolicy.Sha256MetadataName] = sha256;
            await client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            throw Map(exception);
        }
        catch (AmazonServiceException exception)
        {
            throw new S3StorageRemoteException("s3_service_unavailable", "S3 could not be reached.", exception);
        }
    }

    private static AmazonS3Client CreateClient(S3StorageSettings settings)
    {
        var credentials = new BasicAWSCredentials(settings.AccessKey, settings.SecretKey);
        var config = new AmazonS3Config { ForcePathStyle = true };
        if (string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }
        else
        {
            config.ServiceURL = settings.PublicBaseUrl;
            config.AuthenticationRegion = settings.Region;
        }
        return new AmazonS3Client(credentials, config);
    }

    private static S3StorageRemoteException Map(AmazonS3Exception exception) => exception.StatusCode switch
    {
        HttpStatusCode.Forbidden => new("s3_access_denied", "S3 denied access. Check credentials, bucket policy, and public-read ACL permissions.", exception),
        HttpStatusCode.Unauthorized => new("s3_credentials_invalid", "S3 credentials were rejected.", exception),
        _ => new("s3_request_failed", "The S3 request failed.", exception)
    };

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        _ => "application/octet-stream"
    };
}
