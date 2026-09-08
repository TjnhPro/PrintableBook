using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Core.Tests.Application.Updates;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckAsyncReturnsAvailableWhenLatestVersionIsNewer()
    {
        var current = new StubVersionProvider(new Version(0, 1, 1));
        var latest = Release("0.1.2");
        var feed = new StubUpdateFeed(latest);
        var service = new UpdateService(feed, current);

        var result = await service.CheckAsync();

        Assert.Equal(UpdateAvailability.Available, result.Availability);
        Assert.Equal(new Version(0, 1, 1), result.CurrentVersion);
        Assert.Same(latest, result.LatestRelease);
    }

    [Theory]
    [InlineData("0.1.1")]
    [InlineData("0.1.0")]
    public async Task CheckAsyncReturnsUpToDateWhenLatestIsNotNewer(string latestVersion)
    {
        var service = new UpdateService(
            new StubUpdateFeed(Release(latestVersion)),
            new StubVersionProvider(new Version(0, 1, 1)));

        var result = await service.CheckAsync();

        Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
    }

    [Fact]
    public async Task CheckAsyncReturnsUpToDateWhenNoStableReleaseExists()
    {
        var service = new UpdateService(
            new StubUpdateFeed(null),
            new StubVersionProvider(new Version(0, 1, 1)));

        var result = await service.CheckAsync();

        Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
        Assert.Null(result.LatestRelease);
    }

    [Fact]
    public async Task CheckAsyncPassesCancellationTokenToFeed()
    {
        using var source = new CancellationTokenSource();
        var feed = new StubUpdateFeed(Release("0.1.2"));
        var service = new UpdateService(
            feed,
            new StubVersionProvider(new Version(0, 1, 1)));

        await service.CheckAsync(source.Token);

        Assert.Equal(source.Token, feed.ObservedCancellationToken);
    }

    [Fact]
    public async Task CheckAsyncDoesNotHideFeedFailures()
    {
        var expected = new HttpRequestException("network down");
        var service = new UpdateService(
            new ThrowingUpdateFeed(expected),
            new StubVersionProvider(new Version(0, 1, 1)));

        var actual = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await service.CheckAsync().AsTask());

        Assert.Same(expected, actual);
    }

    private static UpdateInfo Release(string version)
    {
        var archiveName = $"PrintableBook-{version}-win-x64.zip";

        return new UpdateInfo(
            Version.Parse(version),
            $"v{version}",
            $"Printable Book v{version}",
            null,
            new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero),
            new Uri($"https://github.com/TjnhPro/PrintableBook/releases/tag/v{version}"),
            new UpdatePackageInfo(
                new UpdateAssetInfo(
                    archiveName,
                    new Uri($"https://example.test/{archiveName}"),
                    14_000_000),
                new UpdateAssetInfo(
                    $"{archiveName}.sha256",
                    new Uri($"https://example.test/{archiveName}.sha256"),
                    99),
                new string('a', 64),
                new string('b', 64)));
    }

    private sealed class StubVersionProvider(Version currentVersion)
        : IApplicationVersionProvider
    {
        public Version CurrentVersion { get; } = currentVersion;
    }

    private sealed class StubUpdateFeed(UpdateInfo? result) : IUpdateFeed
    {
        public CancellationToken ObservedCancellationToken { get; private set; }

        public ValueTask<UpdateInfo?> GetLatestStableAsync(
            CancellationToken cancellationToken = default)
        {
            ObservedCancellationToken = cancellationToken;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingUpdateFeed(Exception exception) : IUpdateFeed
    {
        public ValueTask<UpdateInfo?> GetLatestStableAsync(
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<UpdateInfo?>(exception);
        }
    }
}
