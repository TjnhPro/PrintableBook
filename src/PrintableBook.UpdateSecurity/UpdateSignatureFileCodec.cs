using System.Text;

namespace PrintableBook.UpdateSecurity;

public static class UpdateSignatureFileCodec
{
    public static byte[] Encode(ReadOnlySpan<byte> signature)
    {
        RequireLength(signature);
        return Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n");
    }

    public static byte[] Decode(ReadOnlySpan<byte> bytes)
    {
        var value = Encoding.ASCII.GetString(bytes);
        if (value.EndsWith("\r\n", StringComparison.Ordinal)) value = value[..^2];
        else if (value.EndsWith("\n", StringComparison.Ordinal)) value = value[..^1];
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)) throw new InvalidDataException("Update signature file is invalid.");
        try
        {
            var signature = Convert.FromBase64String(value);
            if (signature.Length != 64) throw new InvalidDataException("Update signature file is invalid.");
            return signature;
        }
        catch (FormatException exception) { throw new InvalidDataException("Update signature file is invalid.", exception); }
    }

    private static void RequireLength(ReadOnlySpan<byte> value)
    {
        if (value.Length != 64) throw new ArgumentException("An Ed25519 signature must be 64 bytes.", nameof(value));
    }
}
