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
    public async Task GetLatestStableAsyncAuthenticatesMatchingReleaseAssetsAndUsesTheGitHubApiContract()
    {
        var fixture = Fixture.For("0.2.0");
        HttpMethod? method = null;
        string? path = null;
        string? userAgent = null;
        string? accept = null;
        string? apiVersion = null;
        var factory = CreateFactory(fixture, releaseResponse: request =>
        {
            method = request.Method;
            path = request.RequestUri!.AbsolutePath;
            userAgent = request.Headers.UserAgent.ToString();
            accept = request.Headers.Accept.ToString();
            apiVersion = request.Headers.GetValues("X-GitHub-Api-Version").Single();
            return JsonResponse(ReleaseJson("v0.2.0", fixture.Assets));
        });

        var result = await CreateFeed(factory).GetLatestStableAsync();

        Assert.Equal(new Version(0, 2, 0), result!.Version);
        Assert.Equal("v0.2.0", result.TagName);
        Assert.Equal("PrintableBook-0.2.0-win-x64.zip", result.Package.Archive.Name);
        Assert.Equal(fixture.Manifest.Archive.Sha256, result.Package.ArchiveSha256);
        Assert.Equal(fixture.Manifest.Checksum.Sha256, result.Package.ChecksumSha256);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/repos/TjnhPro/PrintableBook/releases/latest", path);
        Assert.Contains("PrintableBook", userAgent, StringComparison.Ordinal);
        Assert.Contains("application/vnd.github+json", accept, StringComparison.Ordinal);
        Assert.Equal("2022-11-28", apiVersion);
    }

    [Fact]
    public async Task GetLatestStableAsyncRequiresExactlyOneOfEachSignedAsset()
    {
        var fixture = Fixture.For("0.2.0");
        foreach (var name in new[] { ".manifest.json\"", ".manifest.json.sig" })
        {
            var matchingAsset = fixture.Assets.Single(asset => asset.Contains(name, StringComparison.Ordinal));
            var missing = fixture.Assets.Where(asset => !asset.Contains(name, StringComparison.Ordinal)).ToArray();
            var duplicate = fixture.Assets.Append(matchingAsset).ToArray();
            await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(fixture, missing)).GetLatestStableAsync().AsTask());
            await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(fixture, duplicate)).GetLatestStableAsync().AsTask());
        }
    }

    [Fact]
    public async Task GetLatestStableAsyncRejectsSignedVersionAndSizeMismatches()
    {
        var releaseFixture = Fixture.For("0.2.0");
        var signedForDifferentVersion = Fixture.For("0.2.1");
        var versionMismatchFactory = CreateFactory(
            signedForDifferentVersion,
            releaseFixture.Assets,
            tag: "v0.2.0",
            manifestRequestUri: releaseFixture.ManifestUri,
            signatureRequestUri: releaseFixture.SignatureUri);

        var versionError = await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(versionMismatchFactory).GetLatestStableAsync().AsTask());
        Assert.Equal("Signed update manifest does not match the GitHub release assets.", versionError.Message);

        var changedArchiveSize = releaseFixture.Assets
            .Select(asset => asset.Contains(".zip\"", StringComparison.Ordinal) ? asset.Replace("\"size\":100", "\"size\":101", StringComparison.Ordinal) : asset)
            .ToArray();
        var sizeError = await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(releaseFixture, changedArchiveSize)).GetLatestStableAsync().AsTask());
        Assert.Equal("Signed update manifest does not match the GitHub release assets.", sizeError.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GetLatestStableAsyncReturnsNullForNonStableRelease(bool draft, bool prerelease)
    {
        var fixture = Fixture.For("0.2.0");
        var factory = CreateFactory(fixture, releaseResponse: _ => JsonResponse(ReleaseJson("v0.2.0", fixture.Assets, draft: draft, prerelease: prerelease)));

        Assert.Null(await CreateFeed(factory).GetLatestStableAsync());
    }

    [Theory]
    [InlineData(null, "https://github.com/TjnhPro/PrintableBook/releases/tag/v0.2.0")]
    [InlineData("not-a-date", "https://github.com/TjnhPro/PrintableBook/releases/tag/v0.2.0")]
    [InlineData("2026-09-08T00:00:00Z", null)]
    [InlineData("2026-09-08T00:00:00Z", "not-a-url")]
    public async Task GetLatestStableAsyncRejectsInvalidRequiredMetadata(string? publishedAt, string? htmlUrl)
    {
        var fixture = Fixture.For("0.2.0");
        var factory = CreateFactory(fixture, releaseResponse: _ => JsonResponse(ReleaseJson("v0.2.0", fixture.Assets, publishedAt: publishedAt, includeHtmlUrl: htmlUrl is not null, htmlUrl: htmlUrl)));

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(factory).GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncPreservesHttpAndJsonFailures()
    {
        var fixture = Fixture.For("0.2.0");
        var httpFactory = CreateFactory(fixture, releaseResponse: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var jsonFactory = CreateFactory(fixture, releaseResponse: _ => JsonResponse("{ invalid json"));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateFeed(httpFactory).GetLatestStableAsync().AsTask());
        await Assert.ThrowsAsync<JsonException>(() => CreateFeed(jsonFactory).GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncFallsBackToTagWhenNameIsMissing()
    {
        var fixture = Fixture.For("0.2.0");
        var factory = CreateFactory(fixture, releaseResponse: _ => JsonResponse(ReleaseJson("v0.2.0", fixture.Assets, includeName: false)));

        var result = await CreateFeed(factory).GetLatestStableAsync();

        Assert.Equal("v0.2.0", result!.Name);
    }

    [Theory]
    [MemberData(nameof(InvalidPackageAssets))]
    public async Task GetLatestStableAsyncRejectsInvalidPackageAssets(string[] assets)
    {
        var fixture = Fixture.For("0.2.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateFeed(CreateFactory(fixture, assets)).GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncIgnoresUnrelatedAssets()
    {
        var fixture = Fixture.For("0.2.0");
        var assets = fixture.Assets.Append(Fixture.Asset("notes.txt", 42)).Append(Fixture.Asset("diagnostics.json", 17)).ToArray();

        var result = await CreateFeed(CreateFactory(fixture, assets)).GetLatestStableAsync();

        Assert.NotNull(result);
        Assert.Equal("PrintableBook-0.2.0-win-x64.zip", result!.Package.Archive.Name);
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

    public static IEnumerable<object[]> InvalidPackageAssets()
    {
        var fixture = Fixture.For("0.2.0");
        var archive = fixture.Assets[0];
        var checksum = fixture.Assets[1];
        var metadata = fixture.Assets.Skip(2).ToArray();
        string[] WithMetadata(params string[] packageAssets) => packageAssets.Concat(metadata).ToArray();
        yield return new object[] { fixture.Assets.Where(asset => asset != archive).ToArray() };
        yield return new object[] { fixture.Assets.Where(asset => asset != checksum).ToArray() };
        yield return new object[] { WithMetadata(archive.Replace("\"size\":100", "\"size\":0", StringComparison.Ordinal), checksum) };
        yield return new object[] { WithMetadata(archive, checksum.Replace("\"size\":99", "\"size\":0", StringComparison.Ordinal)) };
        yield return new object[] { WithMetadata(Fixture.AssetWithNullableUrl(fixture.Manifest.Archive.Name, 100, null), checksum) };
        yield return new object[] { WithMetadata(archive, Fixture.AssetWithNullableUrl(fixture.Manifest.Checksum.Name, 99, "not-a-url")) };
        yield return new object[] { WithMetadata(Fixture.Asset("PrintableBook-9.9.9-win-x64.zip", 100), Fixture.Asset("PrintableBook-9.9.9-win-x64.zip.sha256", 99)) };
        yield return new object[] { WithMetadata(archive, archive, checksum) };
    }

    private static GitHubReleaseUpdateFeed CreateFeed(IHttpClientFactory factory) =>
        new(factory, new SignedReleaseManifestClient(new StubPublicKeyProvider(Ed25519UpdateSignature.DerivePublicKey(TestSeed))));

    private static StubHttpClientFactory CreateFactory(
        Fixture responseFixture,
        IReadOnlyCollection<string>? assets = null,
        string? tag = null,
        Uri? manifestRequestUri = null,
        Uri? signatureRequestUri = null,
        Func<HttpRequestMessage, HttpResponseMessage>? releaseResponse = null)
    {
        var expectedManifestUri = manifestRequestUri ?? responseFixture.ManifestUri;
        var expectedSignatureUri = signatureRequestUri ?? responseFixture.SignatureUri;
        return new StubHttpClientFactory(() => new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == expectedManifestUri) return Bytes(responseFixture.ManifestBytes);
            if (request.RequestUri == expectedSignatureUri) return Bytes(responseFixture.SignatureBytes);
            if (releaseResponse is not null) return releaseResponse(request);
            return JsonResponse(ReleaseJson(tag ?? $"v{responseFixture.ReleaseVersion}", assets ?? responseFixture.Assets));
        })) { BaseAddress = new Uri("https://api.github.com/") });
    }

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag, IReadOnlyCollection<string> assets, bool draft = false, bool prerelease = false, bool includeName = true, string? body = "release notes", string? publishedAt = "2026-09-08T00:00:00Z", bool includeHtmlUrl = true, string? htmlUrl = null) => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        name = includeName ? $"Printable Book {tag}" : null,
        body,
        draft,
        prerelease,
        published_at = publishedAt,
        html_url = includeHtmlUrl ? htmlUrl ?? $"https://github.com/TjnhPro/PrintableBook/releases/tag/{tag}" : null,
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

        public static string Asset(string name, long size, string? downloadUrl = null) => JsonSerializer.Serialize(new { name, size, browser_download_url = downloadUrl ?? $"https://example.test/{name}" });
        public static string AssetWithNullableUrl(string name, long size, string? downloadUrl) => JsonSerializer.Serialize(new { name, size, browser_download_url = downloadUrl });
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
