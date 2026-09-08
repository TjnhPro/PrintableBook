using System.Net;
using System.Text;
using System.Text.Json;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseUpdateFeedTests
{
    [Fact]
    public async Task GetLatestStableAsyncMapsStableGitHubReleaseAndBuildsExpectedRequest()
    {
        HttpMethod? method = null;
        string? path = null;
        string? userAgent = null;
        string? accept = null;
        string? apiVersion = null;
        var factory = CreateFactory((request, _) =>
        {
            method = request.Method;
            path = request.RequestUri!.AbsolutePath;
            userAgent = request.Headers.UserAgent.ToString();
            accept = request.Headers.Accept.ToString();
            apiVersion = request.Headers.GetValues("X-GitHub-Api-Version").Single();
            return JsonResponse(ReleaseJson());
        });
        var feed = new GitHubReleaseUpdateFeed(factory);

        var result = await feed.GetLatestStableAsync();

        Assert.Equal(new Version(0, 1, 2), result!.Version);
        Assert.Equal("v0.1.2", result.TagName);
        Assert.Equal("Printable Book v0.1.2", result.Name);
        Assert.Equal("release notes", result.ReleaseNotes);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), result.PublishedAtUtc);
        Assert.Equal(new Uri("https://github.com/TjnhPro/PrintableBook/releases/tag/v0.1.2"), result.ReleasePageUri);
        Assert.Equal("PrintableBook-0.1.2-win-x64.zip", result.Package.Archive.Name);
        Assert.Equal(14_227_311, result.Package.Archive.SizeBytes);
        Assert.Equal(
            new Uri("https://github.com/TjnhPro/PrintableBook/releases/download/v0.1.2/PrintableBook-0.1.2-win-x64.zip"),
            result.Package.Archive.DownloadUri);
        Assert.Equal("PrintableBook-0.1.2-win-x64.zip.sha256", result.Package.Checksum.Name);
        Assert.Equal(99, result.Package.Checksum.SizeBytes);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/repos/TjnhPro/PrintableBook/releases/latest", path);
        Assert.Contains("PrintableBook", userAgent, StringComparison.Ordinal);
        Assert.Contains("application/vnd.github+json", accept, StringComparison.Ordinal);
        Assert.Equal("2022-11-28", apiVersion);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public async Task GetLatestStableAsyncReturnsNullForNonStableRelease(string field)
    {
        var factory = CreateFactory((_, _) => JsonResponse(ReleaseJson($"\"{field}\": true")));
        var feed = new GitHubReleaseUpdateFeed(factory);

        Assert.Null(await feed.GetLatestStableAsync());
    }

    [Theory]
    [InlineData("0.1.2")]
    [InlineData("v0.1")]
    [InlineData("v0.1.2-beta.1")]
    public async Task GetLatestStableAsyncRejectsInvalidStableTag(string tagName)
    {
        var factory = CreateFactory((_, _) => JsonResponse(ReleaseJson($"\"tag_name\": \"{tagName}\"")));
        var feed = new GitHubReleaseUpdateFeed(factory);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Theory]
    [InlineData("\"published_at\": null")]
    [InlineData("\"published_at\": \"not-a-date\"")]
    [InlineData("\"html_url\": null")]
    [InlineData("\"html_url\": \"not-a-url\"")]
    public async Task GetLatestStableAsyncRejectsInvalidRequiredMetadata(string replacement)
    {
        var factory = CreateFactory((_, _) => JsonResponse(ReleaseJson(replacement)));
        var feed = new GitHubReleaseUpdateFeed(factory);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncPreservesHttpFailures()
    {
        var factory = CreateFactory((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var feed = new GitHubReleaseUpdateFeed(factory);

        await Assert.ThrowsAsync<HttpRequestException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncPreservesJsonSyntaxFailures()
    {
        var factory = CreateFactory((_, _) => JsonResponse("{ invalid json"));
        var feed = new GitHubReleaseUpdateFeed(factory);

        await Assert.ThrowsAsync<JsonException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncFallsBackToTagWhenNameIsMissing()
    {
        var factory = CreateFactory((_, _) => JsonResponse(ReleaseJson("\"name\": null")));
        var feed = new GitHubReleaseUpdateFeed(factory);

        var result = await feed.GetLatestStableAsync();

        Assert.Equal("v0.1.2", result!.Name);
    }

    [Fact]
    public async Task GetLatestStableAsyncCreatesAClientForEachCheck()
    {
        var factory = new StubHttpClientFactory(() => CreateClient((_, _) => JsonResponse(ReleaseJson())));
        var feed = new GitHubReleaseUpdateFeed(factory);

        await feed.GetLatestStableAsync();
        await feed.GetLatestStableAsync();

        Assert.Equal(2, factory.CreateClientCallCount);
    }

    [Theory]
    [MemberData(nameof(InvalidPackageAssets))]
    public async Task GetLatestStableAsyncRejectsMissingOrInvalidRequiredPackageAssets(string assetsJson)
    {
        var factory = CreateFactory((_, _) => JsonResponse(ReleaseJson(assetsJson: assetsJson)));
        var feed = new GitHubReleaseUpdateFeed(factory);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncIgnoresUnrelatedAssets()
    {
        var assets = AssetsJson(
            ArchiveAsset(),
            ChecksumAsset(),
            AssetJson("notes.txt", 42, "https://example.test/notes.txt"),
            AssetJson("update-manifest.json", 99, "https://example.test/update-manifest.json"));
        var factory = CreateFactory((_, _) => JsonResponse(ReleaseJson(assetsJson: assets)));
        var feed = new GitHubReleaseUpdateFeed(factory);

        var result = await feed.GetLatestStableAsync();

        Assert.Equal("PrintableBook-0.1.2-win-x64.zip", result!.Package.Archive.Name);
    }

    public static IEnumerable<object[]> InvalidPackageAssets()
    {
        yield return [AssetsJson(ChecksumAsset())];
        yield return [AssetsJson(ArchiveAsset())];
        yield return [AssetsJson(ArchiveAsset(size: 0), ChecksumAsset())];
        yield return [AssetsJson(ArchiveAsset(), ChecksumAsset(size: 0))];
        yield return [AssetsJson(ArchiveAsset(downloadUrl: null), ChecksumAsset())];
        yield return [AssetsJson(ArchiveAsset(), ChecksumAsset(downloadUrl: "not-a-url"))];
        yield return [AssetsJson(
            AssetJson("PrintableBook-9.9.9-win-x64.zip", 14_000_000, "https://example.test/other.zip"),
            AssetJson("PrintableBook-9.9.9-win-x64.zip.sha256", 99, "https://example.test/other.zip.sha256"))];
        yield return [AssetsJson(ArchiveAsset(), ArchiveAsset(), ChecksumAsset())];
    }

    private static IHttpClientFactory CreateFactory(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        return new StubHttpClientFactory(() => CreateClient(responder));
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        return new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static string ReleaseJson(string? replacement = null, string? assetsJson = null)
    {
        var properties = new[]
        {
            "\"tag_name\": \"v0.1.2\"",
            "\"name\": \"Printable Book v0.1.2\"",
            "\"body\": \"release notes\"",
            "\"draft\": false",
            "\"prerelease\": false",
            "\"published_at\": \"2026-09-08T00:00:00Z\"",
            "\"html_url\": \"https://github.com/TjnhPro/PrintableBook/releases/tag/v0.1.2\"",
            $"\"assets\": {assetsJson ?? AssetsJson(ArchiveAsset(), ChecksumAsset())}",
        };

        if (replacement is not null)
        {
            var name = replacement[..replacement.IndexOf(':')].Trim('"');
            properties = properties.Select(property => property.StartsWith($"\"{name}\":", StringComparison.Ordinal) ? replacement : property).ToArray();
        }

        return $"{{{string.Join(',', properties)}}}";
    }

    private static string ArchiveAsset(long size = 14_227_311, string? downloadUrl = "https://github.com/TjnhPro/PrintableBook/releases/download/v0.1.2/PrintableBook-0.1.2-win-x64.zip")
    {
        return AssetJson("PrintableBook-0.1.2-win-x64.zip", size, downloadUrl);
    }

    private static string ChecksumAsset(long size = 99, string? downloadUrl = "https://github.com/TjnhPro/PrintableBook/releases/download/v0.1.2/PrintableBook-0.1.2-win-x64.zip.sha256")
    {
        return AssetJson("PrintableBook-0.1.2-win-x64.zip.sha256", size, downloadUrl);
    }

    private static string AssetJson(string name, long size, string? downloadUrl)
    {
        return JsonSerializer.Serialize(new
        {
            name,
            size,
            browser_download_url = downloadUrl,
        });
    }

    private static string AssetsJson(params string[] assets)
    {
        return $"[{string.Join(',', assets)}]";
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request, cancellationToken));
        }
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
