using System.Reflection;
using System.Text;

namespace PrintableBook.UpdateSecurity;

public static class ProductionUpdateSigningKey
{
    public static byte[] GetPublicKey()
    {
        var assembly = typeof(ProductionUpdateSigningKey).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".production-update-public-key.txt", StringComparison.Ordinal))
            .ToArray();
        if (resources.Length != 1) throw new InvalidDataException("The production update public key resource is invalid.");

        using var stream = assembly.GetManifestResourceStream(resources[0])
            ?? throw new InvalidDataException("The production update public key resource is missing.");
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
        var value = reader.ReadToEnd().Trim();
        try
        {
            var key = Convert.FromBase64String(value);
            if (key.Length != Ed25519UpdateSignature.PublicKeySize) throw new InvalidDataException("The production update public key resource is invalid.");
            return key;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The production update public key resource is invalid.", exception);
        }
    }
}
