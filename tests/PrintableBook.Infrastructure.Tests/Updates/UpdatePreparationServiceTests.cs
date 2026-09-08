using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class UpdatePreparationServiceTests
{
    private const string ArchiveName = "PrintableBook-0.1.2-win-x64.zip";

    [Fact]
    public async Task PrepareAsyncDownloadsVerifiesAndPromotesValidPayload()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateZipBytes(ValidEntries());
        var update = CreateUpdate(archive, ChecksumFor(archive));
        var progress = new RecordingProgress();
        var service = CreateService(directory.Path, (request, _) => Task.FromResult(ResponseFor(request, update, archive, ChecksumFor(archive))));

        var prepared = await service.PrepareAsync(update, progress);

        var expectedPayload = Path.Combine(directory.Path, "staging", "0.1.2", "payload");
        Assert.Equal(new Version(0, 1, 2), prepared.Version);
        Assert.Equal(expectedPayload, prepared.PayloadDirectoryPath);
        Assert.True(File.Exists(Path.Combine(expectedPayload, "PrintableBook.exe")));
        Assert.True(File.Exists(Path.Combine(expectedPayload, "Frontend", "index.html")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "downloads", "0.1.2", ArchiveName)));
        Assert.True(File.Exists(Path.Combine(directory.Path, "downloads", "0.1.2", $"{ArchiveName}.sha256")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(directory.Path, "staging", "0.1.2"), ".extracting-*"));
        Assert.Equal(UpdatePreparationStage.Ready, progress.Values[^1].Stage);
        Assert.Contains(progress.Values, value => value.Stage == UpdatePreparationStage.DownloadingArchive);
        Assert.Contains(progress.Values, value => value.Stage == UpdatePreparationStage.DownloadingChecksum);
        Assert.Contains(progress.Values, value => value.Stage == UpdatePreparationStage.Verifying);
        Assert.Contains(progress.Values, value => value.Stage == UpdatePreparationStage.Extracting);
        Assert.Contains(progress.Values, value => value.Stage == UpdatePreparationStage.Validating);
    }

    [Fact]
    public async Task PrepareAsyncCleansVersionWorkspaceWhenChecksumMismatches()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateZipBytes(ValidEntries());
        var update = CreateUpdate(archive, $"{new string('0', 64)}  {ArchiveName}");
        var service = CreateService(directory.Path, (request, _) => Task.FromResult(ResponseFor(request, update, archive, $"{new string('0', 64)}  {ArchiveName}")));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.PrepareAsync(update).AsTask());

        AssertVersionWorkspaceIsClean(directory.Path);
    }

    [Fact]
    public async Task PrepareAsyncCleansVersionWorkspaceWhenPackageContractIsInvalid()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateZipBytes(ValidEntries().Append(("settings.json", Bytes("forbidden"))).ToArray());
        var checksum = ChecksumFor(archive);
        var update = CreateUpdate(archive, checksum);
        var service = CreateService(directory.Path, (request, _) => Task.FromResult(ResponseFor(request, update, archive, checksum)));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.PrepareAsync(update).AsTask());

        AssertVersionWorkspaceIsClean(directory.Path);
    }

    [Fact]
    public async Task PrepareAsyncCleansVersionWorkspaceWhenArchiveTraversesPaths()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateZipBytes(("../outside.txt", Bytes("outside")));
        var checksum = ChecksumFor(archive);
        var update = CreateUpdate(archive, checksum);
        var service = CreateService(directory.Path, (request, _) => Task.FromResult(ResponseFor(request, update, archive, checksum)));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.PrepareAsync(update).AsTask());

        AssertVersionWorkspaceIsClean(directory.Path);
        Assert.False(File.Exists(Path.Combine(directory.Path, "staging", "0.1.2", "outside.txt")));
    }

    [Fact]
    public async Task PrepareAsyncCleansVersionWorkspaceWhenCancelled()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateZipBytes(ValidEntries());
        var checksum = ChecksumFor(archive);
        var update = CreateUpdate(archive, checksum);
        var service = CreateService(directory.Path, (request, _) => Task.FromResult(ResponseFor(request, update, archive, checksum)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await service.PrepareAsync(update, cancellationToken: cancellation.Token).AsTask());

        AssertVersionWorkspaceIsClean(directory.Path);
    }

    [Fact]
    public async Task PrepareAsyncRedownloadsWhenPreparingSameVersionAgain()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateZipBytes(ValidEntries());
        var checksum = ChecksumFor(archive);
        var update = CreateUpdate(archive, checksum);
        var requests = 0;
        var service = CreateService(directory.Path, (request, _) =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(ResponseFor(request, update, archive, checksum));
        });

        await service.PrepareAsync(update);
        await service.PrepareAsync(update);

        Assert.Equal(4, requests);
        Assert.True(File.Exists(Path.Combine(directory.Path, "staging", "0.1.2", "payload", "PrintableBook.exe")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(directory.Path, "staging", "0.1.2"), ".extracting-*"));
    }

    [Fact]
    public async Task PrepareAsyncSerializesConcurrentRequests()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateZipBytes(ValidEntries());
        var checksum = ChecksumFor(archive);
        var update = CreateUpdate(archive, checksum);
        var archiveRequests = 0;
        var firstArchiveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstArchive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(directory.Path, async (request, _) =>
        {
            if (request.RequestUri == update.Package.Archive.DownloadUri && Interlocked.Increment(ref archiveRequests) == 1)
            {
                firstArchiveStarted.SetResult();
                await releaseFirstArchive.Task;
            }

            return ResponseFor(request, update, archive, checksum);
        });

        var first = service.PrepareAsync(update).AsTask();
        await firstArchiveStarted.Task;
        var second = service.PrepareAsync(update).AsTask();

        Assert.Equal(1, archiveRequests);
        releaseFirstArchive.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, archiveRequests);
    }

    private static UpdatePreparationService CreateService(
        string root,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        return new UpdatePreparationService(
            new UpdateStorageLayout(new StubRootProvider(root)),
            new HttpUpdateAssetDownloader(new StubHttpClientFactory(() => new HttpClient(new StubHttpMessageHandler(responder)))),
            new Sha256PackageVerifier(),
            new ZipUpdatePackageExtractor(),
            new UpdatePackageContractValidator());
    }

    private static UpdateInfo CreateUpdate(byte[] archive, string checksum)
    {
        return new UpdateInfo(
            new Version(0, 1, 2),
            "v0.1.2",
            "Printable Book v0.1.2",
            null,
            DateTimeOffset.UtcNow,
            new Uri("https://example.test/release"),
            new UpdatePackageInfo(
                new UpdateAssetInfo(ArchiveName, new Uri("https://example.test/archive"), archive.Length),
                new UpdateAssetInfo($"{ArchiveName}.sha256", new Uri("https://example.test/checksum"), Encoding.ASCII.GetByteCount(checksum))));
    }

    private static HttpResponseMessage ResponseFor(HttpRequestMessage request, UpdateInfo update, byte[] archive, string checksum)
    {
        var content = request.RequestUri == update.Package.Archive.DownloadUri
            ? archive
            : Encoding.ASCII.GetBytes(checksum);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
    }

    private static string ChecksumFor(byte[] archive)
    {
        return $"{Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant()}  {ArchiveName}";
    }

    private static (string Path, byte[] Content)[] ValidEntries() =>
    [
        ("PrintableBook.exe", Bytes("exe")),
        ("PrintableBook.Updater.exe", Bytes("updater")),
        ("Frontend/index.html", Bytes("html")),
        ("Frontend/js/app.js", Bytes("js")),
        ("Frontend/assets/printable-book-logo.png", Bytes("logo")),
        ("Frontend/css/app.css", Bytes("css")),
    ];

    private static byte[] CreateZipBytes(params (string Path, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entryData in entries)
            {
                var entry = archive.CreateEntry(entryData.Path);
                using var entryStream = entry.Open();
                entryStream.Write(entryData.Content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static void AssertVersionWorkspaceIsClean(string root)
    {
        Assert.False(Directory.Exists(Path.Combine(root, "downloads", "0.1.2")));
        Assert.False(Directory.Exists(Path.Combine(root, "staging", "0.1.2")));
    }

    private sealed class StubRootProvider(string root) : IUpdateStorageRootProvider
    {
        public string RootPath { get; } = root;
    }

    private sealed class StubHttpClientFactory(Func<HttpClient> createClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => createClient();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken);
    }

    private sealed class RecordingProgress : IProgress<UpdatePreparationProgress>
    {
        public List<UpdatePreparationProgress> Values { get; } = [];

        public void Report(UpdatePreparationProgress value) => Values.Add(value);
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
