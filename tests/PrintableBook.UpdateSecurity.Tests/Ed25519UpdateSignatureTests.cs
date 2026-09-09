using System.Text;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.UpdateSecurity.Tests;

public sealed class Ed25519UpdateSignatureTests
{
    private static readonly byte[] TestSeed = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void SignsAndVerifiesWithDeterministicFixtureKey()
    {
        var data = Encoding.UTF8.GetBytes("PrintableBook signed update");
        var publicKey = Ed25519UpdateSignature.DerivePublicKey(TestSeed);
        var signature = Ed25519UpdateSignature.Sign(data, TestSeed);

        Assert.Equal(32, publicKey.Length);
        Assert.Equal(64, signature.Length);
        Assert.True(Ed25519UpdateSignature.Verify(data, signature, publicKey));
    }

    [Fact]
    public void VerificationRejectsTampering()
    {
        var data = Encoding.UTF8.GetBytes("PrintableBook signed update");
        var publicKey = Ed25519UpdateSignature.DerivePublicKey(TestSeed);
        var signature = Ed25519UpdateSignature.Sign(data, TestSeed);
        var changedData = data.ToArray();
        var changedSignature = signature.ToArray();
        changedData[0] ^= 1;
        changedSignature[0] ^= 1;
        var otherPublicKey = Ed25519UpdateSignature.DerivePublicKey(Enumerable.Repeat((byte)9, 32).ToArray());

        Assert.False(Ed25519UpdateSignature.Verify(changedData, signature, publicKey));
        Assert.False(Ed25519UpdateSignature.Verify(data, changedSignature, publicKey));
        Assert.False(Ed25519UpdateSignature.Verify(data, signature, otherPublicKey));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void RejectsInvalidPrivateSeedLengths(int length)
    {
        var seed = new byte[length];
        Assert.Throws<ArgumentException>(() => Ed25519UpdateSignature.DerivePublicKey(seed));
        Assert.Throws<ArgumentException>(() => Ed25519UpdateSignature.Sign([], seed));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void RejectsInvalidPublicKeyLengths(int length) =>
        Assert.Throws<ArgumentException>(() => Ed25519UpdateSignature.Verify([], new byte[64], new byte[length]));

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void RejectsInvalidSignatureLengths(int length) =>
        Assert.Throws<ArgumentException>(() => Ed25519UpdateSignature.Verify([], new byte[length], new byte[32]));
}
