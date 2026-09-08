using System.Text;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.UpdateSecurity.Tests;

public sealed class UpdateSignatureFileCodecTests
{
    private static readonly byte[] Signature = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();

    [Fact]
    public void EncodeUsesAsciiBase64AndOneFinalLineFeed()
    {
        var bytes = UpdateSignatureFileCodec.Encode(Signature);

        Assert.Equal(Convert.ToBase64String(Signature) + "\n", Encoding.ASCII.GetString(bytes));
        Assert.Equal(Signature, UpdateSignatureFileCodec.Decode(bytes));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("not base64!\n")]
    [InlineData(" AAAA\n")]
    [InlineData("AAAA AAAA\n")]
    [InlineData("AAAA\nextra")]
    [InlineData("AAAA\n\n")]
    public void DecodeRejectsMalformedText(string value) =>
        Assert.Throws<InvalidDataException>(() => UpdateSignatureFileCodec.Decode(Encoding.ASCII.GetBytes(value)));

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void DecodeRejectsWrongSignatureLength(int length)
    {
        var encoded = Convert.ToBase64String(Enumerable.Repeat((byte)1, length).ToArray()) + "\n";

        Assert.Throws<InvalidDataException>(() => UpdateSignatureFileCodec.Decode(Encoding.ASCII.GetBytes(encoded)));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void DecodePermitsOnlyOneFinalNewlineConvention(string newline)
    {
        var bytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(Signature) + newline);

        Assert.Equal(Signature, UpdateSignatureFileCodec.Decode(bytes));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void EncodeRejectsWrongSignatureLength(int length) =>
        Assert.Throws<ArgumentException>(() => UpdateSignatureFileCodec.Encode(new byte[length]));
}
