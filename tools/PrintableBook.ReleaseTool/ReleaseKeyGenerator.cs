using System.Security.Cryptography;
using System.Text;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool;

public sealed record GeneratedKeyPaths(string PublicKeyPath, string PrivateKeyPath);

public static class ReleaseKeyGenerator
{
    public static GeneratedKeyPaths Generate(string publicOutputPath, string privateOutputPath)
    {
        if (string.IsNullOrWhiteSpace(publicOutputPath)) throw new ArgumentException("A public key output path is required.", nameof(publicOutputPath));
        if (string.IsNullOrWhiteSpace(privateOutputPath)) throw new ArgumentException("A private key output path is required.", nameof(privateOutputPath));
        var publicPath = Path.GetFullPath(publicOutputPath);
        var privatePath = Path.GetFullPath(privateOutputPath);
        if (Path.Equals(publicPath, privatePath)) throw new ArgumentException("Public and private key paths must differ.");
        if (File.Exists(publicPath) || File.Exists(privatePath)) throw new IOException("Refusing to overwrite an existing signing key file.");

        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
        var privateSeed = RandomNumberGenerator.GetBytes(Ed25519UpdateSignature.PrivateSeedSize);
        var publicKey = Ed25519UpdateSignature.DerivePublicKey(privateSeed);
        File.WriteAllText(publicPath, Convert.ToBase64String(publicKey) + Environment.NewLine, Encoding.ASCII);
        File.WriteAllText(privatePath, Convert.ToBase64String(privateSeed) + Environment.NewLine, Encoding.ASCII);
        return new GeneratedKeyPaths(publicPath, privatePath);
    }
}
