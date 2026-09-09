using PrintableBook.UpdateSecurity;

namespace PrintableBook.Infrastructure.Updates;

public sealed class ProductionUpdateManifestPublicKeyProvider : IUpdateManifestPublicKeyProvider
{
    public ReadOnlyMemory<byte> GetPublicKey() => ProductionUpdateSigningKey.GetPublicKey();
}
