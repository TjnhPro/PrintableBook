using System.Text;

namespace PrintableBook.UpdateSecurity;

public static class UpdateChecksumFileCodec
{
    public static string Parse(ReadOnlySpan<byte> bytes, string expectedArchiveName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArchiveName);
        var value = Encoding.ASCII.GetString(bytes);
        if (value.EndsWith("\r\n", StringComparison.Ordinal)) value = value[..^2];
        else if (value.EndsWith("\n", StringComparison.Ordinal)) value = value[..^1];
        if (value.Length != 66 + expectedArchiveName.Length || value[64..66] != "  " || value[66..] != expectedArchiveName || !IsLowercaseSha256(value[..64]))
        {
            throw new InvalidDataException("Update checksum file is invalid.");
        }

        return value[..64];
    }

    public static bool IsLowercaseSha256(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
