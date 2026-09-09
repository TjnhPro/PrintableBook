using System.Text;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.UpdateSecurity.Tests;

public sealed class UpdateChecksumFileCodecTests
{
    private const string ArchiveName = "PrintableBook-0.2.0-win-x64.zip";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("")]
    public void ParsesOnlyStrictSingleLineChecksumText(string newline)
    {
        var bytes = Encoding.ASCII.GetBytes($"{Hash}  {ArchiveName}{newline}");

        Assert.Equal(Hash, UpdateChecksumFileCodec.Parse(bytes, ArchiveName));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA  PrintableBook-0.2.0-win-x64.zip")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa PrintableBook-0.2.0-win-x64.zip")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  other.zip")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  PrintableBook-0.2.0-win-x64.zip\nextra")]
    public void RejectsMalformedChecksumText(string value) =>
        Assert.Throws<InvalidDataException>(() => UpdateChecksumFileCodec.Parse(Encoding.ASCII.GetBytes(value), ArchiveName));
}
