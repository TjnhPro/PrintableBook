using System.Net;
using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.Updates;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class SignedReleaseManifestClientTests
{
    private static readonly byte[] TestSeed = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly Uri ManifestUri = new("https://example.test/update.manifest.json");
    private static readonly Uri SignatureUri = new("https://example.test/update.manifest.json.sig");

    [Fact]
    public async Task VerifiesSignedManifestBeforeReturningIt()
    {
        var manifestBytes = UpdateManifestCodec.Serialize(ValidManifest());
        var signatureBytes = UpdateSignatureFileCodec.Encode(Ed25519UpdateSignature.Sign(manifestBytes, TestSeed));
        var client = CreateClient(manifestBytes, signatureBytes, Ed25519UpdateSignature.DerivePublicKey(TestSeed));

        var result = await client.Client.GetVerifiedAsync(client.HttpClient, Asset("manifest", ManifestUri, manifestBytes), Asset("signature", SignatureUri, signatureBytes));

        Assert.Equal("0.2.0", result.Version);
    }

    [Fact]
    public async Task RejectsInvalidSignatureBeforeAttemptingJsonParsing()
    {
        var manifestBytes = Encoding.UTF8.GetBytes("{ malformed json");
        var signatureBytes = UpdateSignatureFileCodec.Encode(new byte[64]);
        var client = CreateClient(manifestBytes, signatureBytes, Ed25519UpdateSignature.DerivePublicKey(TestSeed));

        await Assert.ThrowsAsync<CryptographicException>(() => client.Client.GetVerifiedAsync(client.HttpClient, Asset("manifest", ManifestUri, manifestBytes), Asset("signature", SignatureUri, signatureBytes)).AsTask());
    }

    [Fact]
    public async Task RejectsTamperingWrongKeysAndReportedSizeMismatches()
    {
        var manifestBytes = UpdateManifestCodec.Serialize(ValidManifest());
        var signatureBytes = UpdateSignatureFileCodec.Encode(Ed25519UpdateSignature.Sign(manifestBytes, TestSeed));
        var tampered = manifestBytes.ToArray();
        tampered[^1] ^= 1;
        var tamperedClient = CreateClient(tampered, signatureBytes, Ed25519UpdateSignature.DerivePublicKey(TestSeed));
        var wrongKeyClient = CreateClient(manifestBytes, signatureBytes, Ed25519UpdateSignature.DerivePublicKey(Enumerable.Repeat((byte)9, 32).ToArray()));
        var correctClient = CreateClient(manifestBytes, signatureBytes, Ed25519UpdateSignature.DerivePublicKey(TestSeed));

        await Assert.ThrowsAsync<CryptographicException>(() => tamperedClient.Client.GetVerifiedAsync(tamperedClient.HttpClient, Asset("manifest", ManifestUri, tampered), Asset("signature", SignatureUri, signatureBytes)).AsTask());
        await Assert.ThrowsAsync<CryptographicException>(() => wrongKeyClient.Client.GetVerifiedAsync(wrongKeyClient.HttpClient, Asset("manifest", ManifestUri, manifestBytes), Asset("signature", SignatureUri, signatureBytes)).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => correctClient.Client.GetVerifiedAsync(correctClient.HttpClient, new UpdateAssetInfo("manifest", ManifestUri, manifestBytes.Length - 1), Asset("signature", SignatureUri, signatureBytes)).AsTask());
    }

    [Fact]
    public async Task RejectsAssetMetadataAndStreamsBeyondTheConfiguredLimit()
    {
        var bytes = new byte[SignedReleaseManifestClient.MaximumManifestAssetSizeBytes + 1];
        var signature = UpdateSignatureFileCodec.Encode(new byte[64]);
        var client = CreateClient(bytes, signature, Ed25519UpdateSignature.DerivePublicKey(TestSeed));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.Client.GetVerifiedAsync(client.HttpClient, new UpdateAssetInfo("manifest", ManifestUri, bytes.Length), Asset("signature", SignatureUri, signature)).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => client.Client.GetVerifiedAsync(client.HttpClient, new UpdateAssetInfo("manifest", ManifestUri, 1), Asset("signature", SignatureUri, signature)).AsTask());
    }

    private static UpdateAssetInfo Asset(string name, Uri uri, byte[] bytes) => new(name, uri, bytes.LongLength);
    private static UpdateReleaseManifest ValidManifest() => new(1, "PrintableBook", "0.2.0", "win-x64", new UpdateReleaseAsset("PrintableBook-0.2.0-win-x64.zip", 1, new string('a', 64)), new UpdateReleaseAsset("PrintableBook-0.2.0-win-x64.zip.sha256", 1, new string('b', 64)));

    private static (SignedReleaseManifestClient Client, HttpClient HttpClient) CreateClient(byte[] manifest, byte[] signature, byte[] publicKey)
    {
        var client = new HttpClient(new StubHttpMessageHandler(request => request.RequestUri == ManifestUri ? Bytes(manifest) : Bytes(signature)));
        return (new SignedReleaseManifestClient(new StubPublicKeyProvider(publicKey)), client);
    }

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class StubPublicKeyProvider(byte[] publicKey) : IUpdateManifestPublicKeyProvider
    {
        public ReadOnlyMemory<byte> GetPublicKey() => publicKey.ToArray();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
