using System.Security.Cryptography;
using System.Text;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class Sha256PackageVerifierTests : IDisposable
{
    private const string ArchiveName = "PrintableBook-0.1.2-win-x64.zip";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PrintableBook.HashTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifyAsyncAcceptsSidecarBoundToBothSignedHashes()
    {
        var (archive, checksum, archiveHash, checksumHash) = await CreateFixtureAsync([1, 2, 3]);

        await new Sha256PackageVerifier().VerifyAsync(archive, checksum, ArchiveName, archiveHash, checksumHash);
    }

    [Fact]
    public async Task VerifyAsyncRejectsMutuallyAgreeingAttackerFilesWhenArchiveHashIsNotSigned()
    {
        var (archive, checksum, _, checksumHash) = await CreateFixtureAsync([9, 9, 9]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new Sha256PackageVerifier().VerifyAsync(archive, checksum, ArchiveName, Hash([1, 2, 3]), checksumHash).AsTask());
    }

    [Fact]
    public async Task VerifyAsyncRejectsSidecarHashAndDeclaredArchiveHashMismatches()
    {
        var (archive, checksum, archiveHash, _) = await CreateFixtureAsync([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new Sha256PackageVerifier().VerifyAsync(archive, checksum, ArchiveName, archiveHash, new string('a', 64)).AsTask());
        var wrongDeclaration = await WriteAsync("checksum.sha256", Encoding.ASCII.GetBytes($"{new string('a', 64)}  {ArchiveName}\n"));
        var wrongDeclarationHash = Hash(await File.ReadAllBytesAsync(wrongDeclaration));
        await Assert.ThrowsAsync<InvalidDataException>(() => new Sha256PackageVerifier().VerifyAsync(archive, wrongDeclaration, ArchiveName, archiveHash, wrongDeclarationHash).AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa PrintableBook-0.1.2-win-x64.zip")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA  PrintableBook-0.1.2-win-x64.zip")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  other.zip")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  PrintableBook-0.1.2-win-x64.zip\nextra")]
    public async Task VerifyAsyncRetainsStrictChecksumFormat(string text)
    {
        var archive = await WriteAsync(ArchiveName, [1, 2, 3]);
        var checksum = await WriteAsync("checksum.sha256", Encoding.ASCII.GetBytes(text));
        var checksumHash = Hash(await File.ReadAllBytesAsync(checksum));

        await Assert.ThrowsAsync<InvalidDataException>(() => new Sha256PackageVerifier().VerifyAsync(archive, checksum, ArchiveName, Hash([1, 2, 3]), checksumHash).AsTask());
    }

    [Fact]
    public async Task VerifyAsyncHonorsCancellation()
    {
        var (archive, checksum, archiveHash, checksumHash) = await CreateFixtureAsync([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Sha256PackageVerifier().VerifyAsync(archive, checksum, ArchiveName, archiveHash, checksumHash, cancellation.Token).AsTask());
    }

    private async Task<(string Archive, string Checksum, string ArchiveHash, string ChecksumHash)> CreateFixtureAsync(byte[] archiveBytes)
    {
        var archive = await WriteAsync(ArchiveName, archiveBytes);
        var checksum = await WriteAsync("checksum.sha256", Encoding.ASCII.GetBytes($"{Hash(archiveBytes)}  {ArchiveName}\n"));
        return (archive, checksum, Hash(archiveBytes), Hash(await File.ReadAllBytesAsync(checksum)));
    }

    private async Task<string> WriteAsync(string name, byte[] bytes)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
