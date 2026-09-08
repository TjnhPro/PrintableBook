using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Core.Tests.Application.Updates;

public sealed class VersionComparerTests
{
    [Theory]
    [InlineData("0.1.2", "0.1.1", true)]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("1.0.0", "0.9.99", true)]
    [InlineData("0.1.1", "0.1.1", false)]
    [InlineData("0.1.1.0", "0.1.1", false)]
    [InlineData("0.1.1", "0.1.1.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    public void IsNewerComparesMajorMinorPatchOnly(
        string candidateText,
        string currentText,
        bool expected)
    {
        var candidate = Version.Parse(candidateText);
        var current = Version.Parse(currentText);

        Assert.Equal(expected, VersionComparer.IsNewer(candidate, current));
    }

    [Fact]
    public void IsNewerRejectsNullCandidate()
    {
        Assert.Throws<ArgumentNullException>(
            () => VersionComparer.IsNewer(null!, new Version(0, 1, 1)));
    }

    [Fact]
    public void IsNewerRejectsNullCurrent()
    {
        Assert.Throws<ArgumentNullException>(
            () => VersionComparer.IsNewer(new Version(0, 1, 1), null!));
    }
}
