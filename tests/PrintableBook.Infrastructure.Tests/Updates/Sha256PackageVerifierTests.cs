using System.Security.Cryptography;
using System.Text;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class Sha256PackageVerifierTests
{
    private const string ArchiveName = "PrintableBook-0.1.2-win-x64.zip";

    [Fact]
    public async Task VerifyAsyncAcceptsMatchingChecksum()
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("archive-content");
        var archivePath = await WriteFileAsync(directory.Path, ArchiveName, bytes);
        var checksumPath = await WriteFileAsync(
            directory.Path,
            $"{ArchiveName}.sha256",
            Encoding.ASCII.GetBytes($"{Hash(bytes)}  {ArchiveName}"));

        await new Sha256PackageVerifier().VerifyAsync(archivePath, checksumPath, ArchiveName);
    }

    [Theory]
    [MemberData(nameof(MalformedChecksums))]
    public async Task VerifyAsyncRejectsMalformedChecksum(string checksumText)
    {
        using var directory = new TemporaryDirectory();
        var archivePath = await WriteFileAsync(directory.Path, ArchiveName, [1, 2, 3]);
        var checksumPath = await WriteFileAsync(directory.Path, "checksum.sha256", Encoding.ASCII.GetBytes(checksumText));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await new Sha256PackageVerifier().VerifyAsync(archivePath, checksumPath, ArchiveName).AsTask());
    }

    [Fact]
    public async Task VerifyAsyncRejectsHashMismatch()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = await WriteFileAsync(directory.Path, ArchiveName, [1, 2, 3]);
        var checksumPath = await WriteFileAsync(
            directory.Path,
            "checksum.sha256",
            Encoding.ASCII.GetBytes($"{new string('0', 64)}  {ArchiveName}"));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await new Sha256PackageVerifier().VerifyAsync(archivePath, checksumPath, ArchiveName).AsTask());
    }

    [Fact]
    public async Task VerifyAsyncHonorsCancellation()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = await WriteFileAsync(directory.Path, ArchiveName, [1, 2, 3]);
        var checksumPath = await WriteFileAsync(directory.Path, "checksum.sha256", Encoding.ASCII.GetBytes($"{Hash([1, 2, 3])}  {ArchiveName}"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new Sha256PackageVerifier().VerifyAsync(archivePath, checksumPath, ArchiveName, cancellation.Token).AsTask());
    }

    public static IEnumerable<object[]> MalformedChecksums()
    {
        yield return [string.Empty];
        yield return [$"{new string('0', 64)}  {ArchiveName}\n{new string('1', 64)}  {ArchiveName}"];
        yield return [$"{new string('0', 63)}  {ArchiveName}"];
        yield return [$"{new string('0', 65)}  {ArchiveName}"];
        yield return [$"{new string('g', 64)}  {ArchiveName}"];
        yield return [new string('0', 64)];
        yield return [$"{new string('0', 64)}  PrintableBook-9.9.9-win-x64.zip"];
    }

    private static async Task<string> WriteFileAsync(string directory, string name, byte[] bytes)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
