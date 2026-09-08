using System.Net;
using System.Text;
using System.Text.Json;
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
        using var client = CreateClient((request, _) =>
        {
            method = request.Method;
            path = request.RequestUri!.AbsolutePath;
            userAgent = request.Headers.UserAgent.ToString();
            accept = request.Headers.Accept.ToString();
            apiVersion = request.Headers.GetValues("X-GitHub-Api-Version").Single();
            return JsonResponse(ReleaseJson());
        });
        var feed = new GitHubReleaseUpdateFeed(client);

        var result = await feed.GetLatestStableAsync();

        Assert.Equal(new Version(0, 1, 2), result!.Version);
        Assert.Equal("v0.1.2", result.TagName);
        Assert.Equal("Printable Book v0.1.2", result.Name);
        Assert.Equal("release notes", result.ReleaseNotes);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), result.PublishedAtUtc);
        Assert.Equal(new Uri("https://github.com/TjnhPro/PrintableBook/releases/tag/v0.1.2"), result.ReleasePageUri);
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
        using var client = CreateClient((_, _) => JsonResponse(ReleaseJson($"\"{field}\": true")));
        var feed = new GitHubReleaseUpdateFeed(client);

        Assert.Null(await feed.GetLatestStableAsync());
    }

    [Theory]
    [InlineData("0.1.2")]
    [InlineData("v0.1")]
    [InlineData("v0.1.2-beta.1")]
    public async Task GetLatestStableAsyncRejectsInvalidStableTag(string tagName)
    {
        using var client = CreateClient((_, _) => JsonResponse(ReleaseJson($"\"tag_name\": \"{tagName}\"")));
        var feed = new GitHubReleaseUpdateFeed(client);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Theory]
    [InlineData("\"published_at\": null")]
    [InlineData("\"published_at\": \"not-a-date\"")]
    [InlineData("\"html_url\": null")]
    [InlineData("\"html_url\": \"not-a-url\"")]
    public async Task GetLatestStableAsyncRejectsInvalidRequiredMetadata(string replacement)
    {
        using var client = CreateClient((_, _) => JsonResponse(ReleaseJson(replacement)));
        var feed = new GitHubReleaseUpdateFeed(client);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncPreservesHttpFailures()
    {
        using var client = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var feed = new GitHubReleaseUpdateFeed(client);

        await Assert.ThrowsAsync<HttpRequestException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncPreservesJsonSyntaxFailures()
    {
        using var client = CreateClient((_, _) => JsonResponse("{ invalid json"));
        var feed = new GitHubReleaseUpdateFeed(client);

        await Assert.ThrowsAsync<JsonException>(async () => await feed.GetLatestStableAsync().AsTask());
    }

    [Fact]
    public async Task GetLatestStableAsyncFallsBackToTagWhenNameIsMissing()
    {
        using var client = CreateClient((_, _) => JsonResponse(ReleaseJson("\"name\": null")));
        var feed = new GitHubReleaseUpdateFeed(client);

        var result = await feed.GetLatestStableAsync();

        Assert.Equal("v0.1.2", result!.Name);
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

    private static string ReleaseJson(string? replacement = null)
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
        };

        if (replacement is not null)
        {
            var name = replacement[..replacement.IndexOf(':')].Trim('"');
            properties = properties.Select(property => property.StartsWith($"\"{name}\":", StringComparison.Ordinal) ? replacement : property).ToArray();
        }

        return $"{{{string.Join(',', properties)}}}";
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
}
