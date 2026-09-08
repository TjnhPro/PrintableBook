using PrintableBook.UpdateSecurity;

namespace PrintableBook.UpdateSecurity.Tests;

public sealed class ProductionUpdateSigningKeyTests
{
    [Fact]
    public void EmbeddedProductionKeyIsAValidNonMutableEd25519PublicKey()
    {
        var first = ProductionUpdateSigningKey.GetPublicKey();
        var second = ProductionUpdateSigningKey.GetPublicKey();

        Assert.Equal(Ed25519UpdateSignature.PublicKeySize, first.Length);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }
}
