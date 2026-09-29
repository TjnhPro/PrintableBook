using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrintableBook.Core.Application.Desktop;

public interface IKeywordReceiptKeyProvider
{
    ReadOnlyMemory<byte> GetKey();
}

public sealed class ProcessKeywordReceiptKeyProvider : IKeywordReceiptKeyProvider
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    public ReadOnlyMemory<byte> GetKey() => key;
}

public interface IKeywordPreviewReceiptProtector
{
    string Protect(KeywordPreviewReceiptPayload payload);
    KeywordPreviewReceiptPayload Unprotect(string receipt);
    string Digest(string receipt);
}

public sealed class KeywordPreviewReceiptProtector(IKeywordReceiptKeyProvider keyProvider) : IKeywordPreviewReceiptProtector
{
    public const int CurrentReceiptVersion = 1;
    public const int MaximumReceiptCharacters = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Protect(KeywordPreviewReceiptPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var signature = HMACSHA256.HashData(keyProvider.GetKey().Span, bytes);
        return $"{Encode(bytes)}.{Encode(signature)}";
    }

    public KeywordPreviewReceiptPayload Unprotect(string receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt) || receipt.Length > MaximumReceiptCharacters)
        {
            throw InvalidReceipt();
        }
        var parts = receipt.Split('.');
        if (parts.Length != 2) throw InvalidReceipt();
        try
        {
            var payloadBytes = Decode(parts[0]);
            var suppliedSignature = Decode(parts[1]);
            var expectedSignature = HMACSHA256.HashData(keyProvider.GetKey().Span, payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature)) throw InvalidReceipt();
            var payload = JsonSerializer.Deserialize<KeywordPreviewReceiptPayload>(payloadBytes, JsonOptions) ?? throw InvalidReceipt();
            if (payload.ReceiptVersion != CurrentReceiptVersion) throw new KeywordPreviewException(new(
                "keyword_preview_version_unsupported", "This keyword preview was created by an unsupported version. Shuffle again.", "receipt", "shuffle"));
            return payload;
        }
        catch (KeywordPreviewException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw InvalidReceipt();
        }
    }

    public string Digest(string receipt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt))).ToLowerInvariant();

    private static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }

    private static KeywordPreviewException InvalidReceipt() => new(new(
        "keyword_preview_invalid", "The keyword preview is invalid or expired. Shuffle again.", "receipt", "shuffle"));
}
