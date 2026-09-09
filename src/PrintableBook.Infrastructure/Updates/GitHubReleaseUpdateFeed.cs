using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintableBook.Core.Application.Updates;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.Infrastructure.Updates;

public sealed class GitHubReleaseUpdateFeed(
    IHttpClientFactory httpClientFactory,
    SignedReleaseManifestClient signedReleaseManifestClient) : IUpdateFeed
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
        var assets = dto.Assets ?? [];
        var names = UpdateReleaseNames.For(version, UpdateManifestContract.RuntimeIdentifier);
        var archive = GetRequiredAsset(assets, names.Archive);
        var checksum = GetRequiredAsset(assets, names.Checksum);
        var manifestAsset = GetRequiredAsset(assets, names.Manifest);
        var signatureAsset = GetRequiredAsset(assets, names.Signature);
        var manifest = await signedReleaseManifestClient.GetVerifiedAsync(httpClient, manifestAsset, signatureAsset, cancellationToken);
        if (manifest.Version != version.ToString(3) || manifest.RuntimeIdentifier != UpdateManifestContract.RuntimeIdentifier ||
            manifest.Archive.Name != archive.Name || manifest.Archive.SizeBytes != archive.SizeBytes ||
            manifest.Checksum.Name != checksum.Name || manifest.Checksum.SizeBytes != checksum.SizeBytes)
        {
            throw new InvalidDataException("Signed update manifest does not match the GitHub release assets.");
        }

        var package = new UpdatePackageInfo(archive, checksum, manifest.Archive.Sha256, manifest.Checksum.Sha256);

        return new UpdateInfo(
            version,
            tagName,
            dto.Name ?? tagName,
            dto.Body,
            publishedAt,
            releasePageUri,
            package);
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

    private static UpdateAssetInfo GetRequiredAsset(
        IReadOnlyCollection<GitHubReleaseAssetDto> assets,
        string expectedName)
    {
        var matches = assets
            .Where(asset => string.Equals(asset.Name, expectedName, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            throw new InvalidDataException($"GitHub release must contain exactly one '{expectedName}' asset.");
        }

        var asset = matches[0];
        if (asset.Size <= 0)
        {
            throw new InvalidDataException($"GitHub release asset '{expectedName}' has an invalid size.");
        }

        if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) ||
            !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            throw new InvalidDataException($"GitHub release asset '{expectedName}' has an invalid browser_download_url.");
        }

        return new UpdateAssetInfo(expectedName, downloadUri, asset.Size);
    }

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] string? PublishedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] GitHubReleaseAssetDto[]? Assets);

    private sealed record GitHubReleaseAssetDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}
