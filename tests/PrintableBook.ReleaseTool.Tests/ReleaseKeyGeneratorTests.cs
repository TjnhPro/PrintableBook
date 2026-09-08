using System.Text;
using PrintableBook.ReleaseTool;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.ReleaseTool.Tests;

public sealed class ReleaseKeyGeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PrintableBook.ReleaseTool.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GeneratesMatchingAsciiBase64KeyFilesAndRefusesOverwrite()
    {
        var paths = ReleaseKeyGenerator.Generate(Path.Combine(_root, "keys", "public.txt"), Path.Combine(_root, "keys", "private.txt"));
        var publicText = File.ReadAllText(paths.PublicKeyPath, Encoding.ASCII);
        var privateText = File.ReadAllText(paths.PrivateKeyPath, Encoding.ASCII);
        var publicKey = Convert.FromBase64String(publicText.Trim());
        var privateSeed = Convert.FromBase64String(privateText.Trim());

        Assert.EndsWith(Environment.NewLine, publicText, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, privateText, StringComparison.Ordinal);
        Assert.Equal(32, publicKey.Length);
        Assert.Equal(32, privateSeed.Length);
        Assert.Equal(publicKey, Ed25519UpdateSignature.DerivePublicKey(privateSeed));
        Assert.Throws<IOException>(() => ReleaseKeyGenerator.Generate(paths.PublicKeyPath, paths.PrivateKeyPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
