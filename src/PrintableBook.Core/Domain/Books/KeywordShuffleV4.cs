using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PrintableBook.Core.Domain.Books;

public readonly record struct KeywordShuffleSeed
{
    public const int ByteLength = 32;

    public KeywordShuffleSeed(string value)
    {
        _ = Decode(value);
        Value = value;
    }

    public string Value { get; }

    public static KeywordShuffleSeed Create() => FromBytes(RandomNumberGenerator.GetBytes(ByteLength));

    public static KeywordShuffleSeed FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != ByteLength) throw new ArgumentException($"A keyword shuffle seed must contain {ByteLength} bytes.", nameof(value));
        return new KeywordShuffleSeed(Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    }

    public byte[] ToBytes() => Decode(Value);

    private static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The keyword shuffle seed is not valid base64url.", nameof(value), exception);
        }
        if (bytes.Length != ByteLength) throw new ArgumentException($"A keyword shuffle seed must contain {ByteLength} bytes.", nameof(value));
        return bytes;
    }
}

public static class KeywordShuffleV4
{
    public static void Shuffle<T>(IList<T> values, KeywordShuffleSeed seed, string label)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (values.Count < 2) return;

        var sourceSeed = seed.ToBytes();
        var labelBytes = Encoding.UTF8.GetBytes(label.Normalize(NormalizationForm.FormC));
        var streamKeyInput = new byte[sourceSeed.Length + labelBytes.Length];
        sourceSeed.CopyTo(streamKeyInput, 0);
        labelBytes.CopyTo(streamKeyInput, sourceSeed.Length);
        var streamKey = SHA256.HashData(streamKeyInput);
        ulong counter = 0;

        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapIndex = (int)NextBounded(streamKey, ref counter, (uint)(index + 1));
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }

    private static uint NextBounded(ReadOnlySpan<byte> streamKey, ref ulong counter, uint bound)
    {
        if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
        var threshold = unchecked(0u - bound) % bound;
        Span<byte> input = stackalloc byte[40];
        streamKey.CopyTo(input);
        while (true)
        {
            BinaryPrimitives.WriteUInt64BigEndian(input[32..], counter++);
            var block = SHA256.HashData(input);
            var sample = BinaryPrimitives.ReadUInt32BigEndian(block);
            if (sample >= threshold) return sample % bound;
        }
    }
}
