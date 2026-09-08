using System.Security.Cryptography;
using System.Text;

namespace PrintableBook.Infrastructure.Updates;

public sealed class Sha256PackageVerifier
{
    public async ValueTask VerifyAsync(
        string archivePath,
        string checksumPath,
        string expectedArchiveName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArchiveName);
        cancellationToken.ThrowIfCancellationRequested();

        var checksumText = await File.ReadAllTextAsync(
            checksumPath,
            Encoding.ASCII,
            cancellationToken);
        var lines = checksumText
            .Split(['\r', '\n'], StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length != 1)
        {
            throw new InvalidDataException("Checksum sidecar must contain exactly one non-empty line.");
        }

        var tokens = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2 ||
            tokens[0].Length != 64 ||
            !tokens[0].All(Uri.IsHexDigit) ||
            !string.Equals(tokens[1], expectedArchiveName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Checksum sidecar format or archive filename is invalid.");
        }

        var expectedHash = Convert.FromHexString(tokens[0]);
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);

        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw new InvalidDataException("Downloaded archive SHA256 does not match its checksum sidecar.");
        }
    }
}
