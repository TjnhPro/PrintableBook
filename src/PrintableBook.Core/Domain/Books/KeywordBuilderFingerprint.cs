using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PrintableBook.Core.Domain.Books;

public static class KeywordBuilderFingerprint
{
    public static string Input(IReadOnlyList<string> genericKeywords, IReadOnlyList<string> bookKeywords, string? canonicalAdsAsin)
    {
        ArgumentNullException.ThrowIfNull(genericKeywords);
        ArgumentNullException.ThrowIfNull(bookKeywords);
        using var stream = new MemoryStream();
        WriteString(stream, "keyword-builder-input-v4");
        WriteList(stream, genericKeywords);
        WriteList(stream, bookKeywords);
        WriteString(stream, canonicalAdsAsin ?? string.Empty);
        return Hash(stream.ToArray());
    }

    public static string Output(IReadOnlyList<string?> keywordSlots, string? adsKeyword, string? adsAsin)
    {
        ArgumentNullException.ThrowIfNull(keywordSlots);
        using var stream = new MemoryStream();
        WriteString(stream, "keyword-builder-output-v4");
        WriteList(stream, keywordSlots.Select(value => value ?? string.Empty).ToArray());
        WriteString(stream, adsKeyword ?? string.Empty);
        WriteString(stream, adsAsin ?? string.Empty);
        return Hash(stream.ToArray());
    }

    public static string Phrases(string label, IReadOnlyList<string> phrases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(phrases);
        using var stream = new MemoryStream();
        WriteString(stream, label);
        WriteList(stream, phrases);
        return Hash(stream.ToArray());
    }

    private static void WriteList(Stream stream, IReadOnlyList<string> values)
    {
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(count, values.Count);
        stream.Write(count);
        foreach (var value in values) WriteString(stream, value.Normalize(NormalizationForm.FormC));
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
