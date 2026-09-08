using System.Security.Cryptography;
using System.Text;

namespace PrintableBook.Infrastructure.Updates;

public sealed class Sha256PackageVerifier
{
    public async ValueTask VerifyAsync(
        string archivePath,
        string checksumPath,
        string expectedArchiveName,
        string expectedArchiveSha256,
        string expectedChecksumSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArchiveName);
        ValidateHash(expectedArchiveSha256, nameof(expectedArchiveSha256));
        ValidateHash(expectedChecksumSha256, nameof(expectedChecksumSha256));
        cancellationToken.ThrowIfCancellationRequested();

        var checksumBytes = await File.ReadAllBytesAsync(checksumPath, cancellationToken);
        var checksumFileHash = SHA256.HashData(checksumBytes);
        if (!CryptographicOperations.FixedTimeEquals(checksumFileHash, Convert.FromHexString(expectedChecksumSha256))) throw new InvalidDataException("Downloaded checksum sidecar does not match the signed checksum hash.");
        var declaredArchiveHash = ParseChecksum(checksumBytes, expectedArchiveName);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(declaredArchiveHash), Convert.FromHexString(expectedArchiveSha256))) throw new InvalidDataException("Checksum sidecar does not match the signed archive hash.");
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);

        if (!CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(expectedArchiveSha256)))
        {
            throw new InvalidDataException("Downloaded archive SHA256 does not match its checksum sidecar.");
        }
    }

    private static string ParseChecksum(byte[] bytes, string expectedArchiveName)
    {
        var value = Encoding.ASCII.GetString(bytes);
        if (value.EndsWith("\r\n", StringComparison.Ordinal)) value = value[..^2];
        else if (value.EndsWith("\n", StringComparison.Ordinal)) value = value[..^1];
        if (value.Length != 66 + expectedArchiveName.Length || value[64..66] != "  " || value[66..] != expectedArchiveName || !IsLowercaseSha256(value[..64])) throw new InvalidDataException("Checksum sidecar format or archive filename is invalid.");
        return value[..64];
    }

    private static void ValidateHash(string value, string parameterName)
    {
        if (!IsLowercaseSha256(value)) throw new ArgumentException("Expected signed SHA256 must be exactly 64 lowercase hexadecimal characters.", parameterName);
    }

    private static bool IsLowercaseSha256(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
