using System.Security.Cryptography;
using PrintableBook.Core.Application.Updates;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.Infrastructure.Updates;

public sealed class SignedReleaseManifestClient(IUpdateManifestPublicKeyProvider publicKeyProvider)
{
    public const long MaximumManifestAssetSizeBytes = 16 * 1024;
    public const long MaximumSignatureAssetSizeBytes = 1024;

    public async ValueTask<UpdateReleaseManifest> GetVerifiedAsync(HttpClient httpClient, UpdateAssetInfo manifestAsset, UpdateAssetInfo signatureAsset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(manifestAsset);
        ArgumentNullException.ThrowIfNull(signatureAsset);
        var manifestBytes = await ReadAssetAsync(httpClient, manifestAsset, MaximumManifestAssetSizeBytes, cancellationToken);
        var signatureBytes = await ReadAssetAsync(httpClient, signatureAsset, MaximumSignatureAssetSizeBytes, cancellationToken);
        var signature = UpdateSignatureFileCodec.Decode(signatureBytes);
        if (!Ed25519UpdateSignature.Verify(manifestBytes, signature, publicKeyProvider.GetPublicKey().Span))
        {
            throw new CryptographicException("Update manifest signature is invalid.");
        }

        return UpdateManifestCodec.Deserialize(manifestBytes);
    }

    private static async Task<byte[]> ReadAssetAsync(HttpClient httpClient, UpdateAssetInfo asset, long maximumBytes, CancellationToken cancellationToken)
    {
        if (asset.SizeBytes <= 0 || asset.SizeBytes > maximumBytes) throw new InvalidDataException("Signed update metadata asset size is invalid.");
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (bytes.Length + read > maximumBytes) throw new InvalidDataException("Signed update metadata asset exceeds the maximum size.");
            bytes.Write(buffer, 0, read);
        }

        if (bytes.Length != asset.SizeBytes) throw new InvalidDataException("Signed update metadata asset size does not match GitHub metadata.");
        return bytes.ToArray();
    }
}
