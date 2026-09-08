using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Infrastructure.Updates;

public sealed class GitHubReleaseUpdateFeed(IHttpClientFactory httpClientFactory) : IUpdateFeed
{
    public const string HttpClientName = "PrintableBook.GitHub";

    public async ValueTask<UpdateInfo?> GetLatestStableAsync(
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        using var httpClient = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "repos/TjnhPro/PrintableBook/releases/latest");
        request.Headers.UserAgent.ParseAdd("PrintableBook-AutoUpdate");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var dto = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
            stream,
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub release response was empty.");

        if (dto.Draft || dto.Prerelease)
        {
            return null;
        }

        var tagName = dto.TagName
            ?? throw new InvalidDataException("GitHub release tag_name is missing.");
        var version = ParseStableVersion(tagName);
        var publishedAt = ParsePublishedAt(dto.PublishedAt);
        var releasePageUri = ParseReleasePageUri(dto.HtmlUrl);

        return new UpdateInfo(
            version,
            tagName,
            dto.Name ?? tagName,
            dto.Body,
            publishedAt,
            releasePageUri);
    }

    private static Version ParseStableVersion(string tagName)
    {
        if (tagName.Length < 2 || (tagName[0] is not ('v' or 'V')))
        {
            throw new InvalidDataException($"GitHub release tag_name '{tagName}' is not a stable version tag.");
        }

        var versionText = tagName[1..];
        var components = versionText.Split('.');
        if (components.Length != 3 || components.Any(component =>
                !int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out _)) ||
            !Version.TryParse(versionText, out var version) ||
            version.Build < 0 || version.Revision >= 0)
        {
            throw new InvalidDataException($"GitHub release tag_name '{tagName}' is not a stable version tag.");
        }

        return version;
    }

    private static DateTimeOffset ParsePublishedAt(string? publishedAt)
    {
        if (string.IsNullOrWhiteSpace(publishedAt) ||
            !DateTimeOffset.TryParse(
                publishedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var value))
        {
            throw new InvalidDataException("GitHub release published_at is missing or invalid.");
        }

        return value;
    }

    private static Uri ParseReleasePageUri(string? htmlUrl)
    {
        if (string.IsNullOrWhiteSpace(htmlUrl) ||
            !Uri.TryCreate(htmlUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidDataException("GitHub release html_url is missing or invalid.");
        }

        return uri;
    }

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] string? PublishedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
