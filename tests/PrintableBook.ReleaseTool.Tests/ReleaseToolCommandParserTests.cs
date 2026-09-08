using PrintableBook.ReleaseTool;

namespace PrintableBook.ReleaseTool.Tests;

public sealed class ReleaseToolCommandParserTests
{
    [Fact]
    public void ParsesEachExactCommand()
    {
        Assert.Equal(new KeygenCommand("public.txt", "private.txt"), ReleaseToolCommandParser.Parse(["keygen", "--public-output", "public.txt", "--private-output", "private.txt"]));
        Assert.Equal(new SignReleaseCommand("release", new Version(0, 2, 0), "win-x64"), ReleaseToolCommandParser.Parse(["sign-release", "--release-root", "release", "--version", "0.2.0", "--runtime", "win-x64"]));
        Assert.Equal(new VerifyReleaseCommand("release", new Version(0, 2, 0), "win-x64"), ReleaseToolCommandParser.Parse(["verify-release", "--release-root", "release", "--version", "0.2.0", "--runtime", "win-x64"]));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("keygen", "--public-output", "public.txt")]
    [InlineData("keygen", "--public-output", "public.txt", "--public-output", "again.txt", "--private-output", "private.txt")]
    [InlineData("keygen", "--unknown", "value", "--public-output", "public.txt", "--private-output", "private.txt")]
    [InlineData("sign-release", "--release-root", "release", "--version", "0.2", "--runtime", "win-x64")]
    [InlineData("sign-release", "--release-root", "release", "--version", "0.2.0.1", "--runtime", "win-x64")]
    [InlineData("sign-release", "--release-root", "release", "--version", "0.2.0", "--runtime", "linux-x64")]
    [InlineData("keygen", "--public-output", "", "--private-output", "private.txt")]
    public void RejectsInvalidCommandLines(params string[] args) => Assert.Throws<ArgumentException>(() => ReleaseToolCommandParser.Parse(args));
}
