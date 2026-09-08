using System.Net;
using System.Text;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class HttpUpdateAssetDownloaderTests
{
    [Fact]
    public async Task DownloadAsyncStreamsAssetToFinalPathAfterCompleteTransfer()
    {
        var bytes = Encoding.UTF8.GetBytes("package-content");
        var asset = Asset(bytes.Length);
        HttpMethod? method = null;
        Uri? uri = null;
        var factory = new StubHttpClientFactory(() => CreateClient((request, _) =>
        {
            method = request.Method;
            uri = request.RequestUri;
            return Task.FromResult(Response(bytes));
        }));
        var downloader = new HttpUpdateAssetDownloader(factory);
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, asset.Name);

        await downloader.DownloadAsync(asset, destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists($"{destination}.partial"));
        Assert.Equal(HttpUpdateAssetDownloader.HttpClientName, factory.LastClientName);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal(asset.DownloadUri, uri);
    }

    [Fact]
    public async Task DownloadAsyncRejectsTruncatedAssetAndRemovesPartialFile()
    {
        var factory = new StubHttpClientFactory(() => CreateClient((_, _) => Task.FromResult(Response(Encoding.UTF8.GetBytes("short")))));
        var downloader = new HttpUpdateAssetDownloader(factory);
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "package.zip");

        await Assert.ThrowsAsync<InvalidDataException>(async () => await downloader.DownloadAsync(Asset(100), destination).AsTask());

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists($"{destination}.partial"));
    }

    [Fact]
    public async Task DownloadAsyncPreservesHttpFailuresAndRemovesPartialFile()
    {
        var factory = new StubHttpClientFactory(() => CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var downloader = new HttpUpdateAssetDownloader(factory);
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "package.zip");

        await Assert.ThrowsAsync<HttpRequestException>(async () => await downloader.DownloadAsync(Asset(1), destination).AsTask());

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists($"{destination}.partial"));
    }

    [Fact]
    public async Task DownloadAsyncHonorsCancellationAndRemovesPartialFile()
    {
        var factory = new StubHttpClientFactory(() => CreateClient((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Response([1]));
        }));
        var downloader = new HttpUpdateAssetDownloader(factory);
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "package.zip");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await downloader.DownloadAsync(Asset(1), destination, cancellationToken: cancellation.Token).AsTask());

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists($"{destination}.partial"));
    }

    [Fact]
    public async Task DownloadAsyncReportsFinalByteCount()
    {
        var bytes = Encoding.UTF8.GetBytes("package-content");
        var factory = new StubHttpClientFactory(() => CreateClient((_, _) => Task.FromResult(Response(bytes))));
        var downloader = new HttpUpdateAssetDownloader(factory);
        var progress = new RecordingProgress();
        using var directory = new TemporaryDirectory();

        await downloader.DownloadAsync(Asset(bytes.Length), Path.Combine(directory.Path, "package.zip"), progress);

        Assert.NotEmpty(progress.Values);
        Assert.Equal(bytes.Length, progress.Values[^1]);
    }

    private static UpdateAssetInfo Asset(long size)
    {
        return new UpdateAssetInfo(
            "PrintableBook-0.1.2-win-x64.zip",
            new Uri("https://example.test/package.zip"),
            size);
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        return new HttpClient(new StubHttpMessageHandler(responder));
    }

    private static HttpResponseMessage Response(byte[] bytes)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }

    private sealed class StubHttpClientFactory(Func<HttpClient> createClient) : IHttpClientFactory
    {
        public int CreateClientCallCount { get; private set; }
        public string? LastClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            LastClientName = name;
            CreateClientCallCount++;
            return createClient();
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responder(request, cancellationToken);
        }
    }

    private sealed class RecordingProgress : IProgress<long>
    {
        public List<long> Values { get; } = [];

        public void Report(long value)
        {
            Values.Add(value);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PrintableBook.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
