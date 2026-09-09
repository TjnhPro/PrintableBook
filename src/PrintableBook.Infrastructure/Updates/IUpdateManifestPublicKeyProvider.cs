namespace PrintableBook.Infrastructure.Updates;

public interface IUpdateManifestPublicKeyProvider
{
    ReadOnlyMemory<byte> GetPublicKey();
}
