using System.Net;
using System.Text;
using System.Text.Json;
using PrintableBook.Infrastructure.Updates;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseUpdateFeedTests
{
    private static readonly byte[] TestSeed = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task GetLatestStableAsyncAuthenticatesMatchingReleaseAssets()
    {
        var fixture = Fixture.For("0.2.0");
        var factory = CreateFactory(fixture);
        var feed = CreateFeed(factory);

        var result = await feed.GetLatestStableAsync();

        Assert.Equal(new Version(0, 2, 0), result!.Version);
        Assert.Equal("v0.2.0", result.TagName);
        Assert.Equal("PrintableBook-0.2.0-win-x64.zip", result.Package.Archive.Name);
        Assert.Equal(fixture.Manifest.Archive.Sha256, result.Package.ArchiveSha256);
        Assert.Equal(fixture.Manifest.Checksum.Sha256, result.Package.ChecksumSha256);
    }

    [Fact]
    public async Task GetLatestStableAsyncRequiresExactlyOneOfEachSignedAsset()
    {
        var fixture = Fixture.For("0.2.0");
        var missingManifest = fixture.Assets.Where(asset => !asset.Contains("manifest.json\"", StringComparison.Ordinal)).ToArray();
        var duplicateSignature = fixture.Assets.Append(fixture.Assets.Single(asset => asset.Contains("manifest.json.sig", StringComparison.Ordinal))).ToArray();

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(fixture, missingManifest)).GetLatestStableAsync().AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(fixture, duplicateSignature)).GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncRejectsSignedVersionAndSizeMismatches()
    {
        var signedForDifferentVersion = Fixture.For("0.2.1");
        var releaseAssets = Fixture.For("0.2.0").Assets;
        var versionMismatchFactory = CreateFactory(signedForDifferentVersion, releaseAssets, tag: "v0.2.0");
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(versionMismatchFactory).GetLatestStableAsync().AsTask());

        var fixture = Fixture.For("0.2.0");
        var changedArchiveSize = fixture.Assets.Select(asset => asset.Contains(".zip\"", StringComparison.Ordinal) ? asset.Replace("\"size\":100", "\"size\":101", StringComparison.Ordinal) : asset).ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(fixture, changedArchiveSize)).GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncCreatesOneFreshClientPerCheck()
    {
        var fixture = Fixture.For("0.2.0");
        var factory = CreateFactory(fixture);
        var feed = CreateFeed(factory);

        await feed.GetLatestStableAsync();
        await feed.GetLatestStableAsync();

        Assert.Equal(2, factory.CreateClientCallCount);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("v0.2")]
    [InlineData("v0.2.0-preview")]
    public async Task GetLatestStableAsyncRejectsInvalidStableTags(string tag)
    {
        var fixture = Fixture.For("0.2.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(fixture, tag: tag)).GetLatestStableAsync().AsTask());
    }

    private static GitHubReleaseUpdateFeed CreateFeed(IHttpClientFactory factory) =>
        new(factory, new SignedReleaseManifestClient(new StubPublicKeyProvider(Ed25519UpdateSignature.DerivePublicKey(TestSeed))));

    private static StubHttpClientFactory CreateFactory(Fixture fixture, IReadOnlyCollection<string>? assets = null, string? tag = null)
    {
        return new StubHttpClientFactory(() => new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == fixture.ManifestUri) return Bytes(fixture.ManifestBytes);
            if (request.RequestUri == fixture.SignatureUri) return Bytes(fixture.SignatureBytes);
            return JsonResponse(ReleaseJson(tag ?? $"v{fixture.ReleaseVersion}", assets ?? fixture.Assets));
        })) { BaseAddress = new Uri("https://api.github.com/") });
    }

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag, IReadOnlyCollection<string> assets) => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        name = $"Printable Book {tag}",
        body = "release notes",
        draft = false,
        prerelease = false,
        published_at = "2026-09-08T00:00:00Z",
        html_url = $"https://github.com/TjnhPro/PrintableBook/releases/tag/{tag}",
        assets = assets.Select(item => JsonDocument.Parse(item).RootElement).ToArray()
    });

    private sealed record Fixture(string ReleaseVersion, UpdateReleaseManifest Manifest, byte[] ManifestBytes, byte[] SignatureBytes, Uri ManifestUri, Uri SignatureUri, string[] Assets)
    {
        public static Fixture For(string version)
        {
            var names = UpdateReleaseNames.For(Version.Parse(version), "win-x64");
            var manifest = new UpdateReleaseManifest(1, "PrintableBook", version, "win-x64", new UpdateReleaseAsset(names.Archive, 100, new string('a', 64)), new UpdateReleaseAsset(names.Checksum, 99, new string('b', 64)));
            var manifestBytes = UpdateManifestCodec.Serialize(manifest);
            var signatureBytes = UpdateSignatureFileCodec.Encode(Ed25519UpdateSignature.Sign(manifestBytes, TestSeed));
            var manifestUri = new Uri($"https://example.test/{names.Manifest}");
            var signatureUri = new Uri($"https://example.test/{names.Signature}");
            return new Fixture(version, manifest, manifestBytes, signatureBytes, manifestUri, signatureUri,
            [
                Asset(names.Archive, 100),
                Asset(names.Checksum, 99),
                Asset(names.Manifest, manifestBytes.Length),
                Asset(names.Signature, signatureBytes.Length)
            ]);
        }

        private static string Asset(string name, long size) => JsonSerializer.Serialize(new { name, size, browser_download_url = $"https://example.test/{name}" });
    }

    private sealed class StubPublicKeyProvider(byte[] key) : IUpdateManifestPublicKeyProvider
    {
        public ReadOnlyMemory<byte> GetPublicKey() => key.ToArray();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class StubHttpClientFactory(Func<HttpClient> createClient) : IHttpClientFactory
    {
        public int CreateClientCallCount { get; private set; }
        public HttpClient CreateClient(string name)
        {
            CreateClientCallCount++;
            return createClient();
        }
    }
}
