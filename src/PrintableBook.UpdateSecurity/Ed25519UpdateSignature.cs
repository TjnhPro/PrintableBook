using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace PrintableBook.UpdateSecurity;

public static class Ed25519UpdateSignature
{
    public const int PrivateSeedSize = 32;
    public const int PublicKeySize = 32;
    public const int SignatureSize = 64;

    public static byte[] DerivePublicKey(ReadOnlySpan<byte> privateSeed)
    {
        RequireLength(privateSeed, PrivateSeedSize, nameof(privateSeed));
        var privateKey = new Ed25519PrivateKeyParameters(privateSeed.ToArray());
        return privateKey.GeneratePublicKey().GetEncoded();
    }

    public static byte[] Sign(ReadOnlySpan<byte> data, ReadOnlySpan<byte> privateSeed)
    {
        RequireLength(privateSeed, PrivateSeedSize, nameof(privateSeed));
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateSeed.ToArray()));
        var payload = data.ToArray();
        signer.BlockUpdate(payload, 0, payload.Length);
        return signer.GenerateSignature();
    }

    public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
    {
        RequireLength(signature, SignatureSize, nameof(signature));
        RequireLength(publicKey, PublicKeySize, nameof(publicKey));
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey.ToArray()));
        var payload = data.ToArray();
        verifier.BlockUpdate(payload, 0, payload.Length);
        return verifier.VerifySignature(signature.ToArray());
    }

    private static void RequireLength(ReadOnlySpan<byte> value, int expectedLength, string parameterName)
    {
        if (value.Length != expectedLength)
        {
            throw new ArgumentException($"Expected {expectedLength} bytes.", parameterName);
        }
    }
}
