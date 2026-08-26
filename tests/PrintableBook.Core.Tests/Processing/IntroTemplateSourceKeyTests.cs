using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class IntroTemplateSourceKeyTests
{
    [Fact]
    public void FromTemplateRoot_returns_a_slash_normalized_relative_key()
    {
        var key = IntroTemplateSourceKey.FromTemplateRoot(
            new DirectoryReference(Path.Combine("C:", "PrintableBook", "brands", "Demo", "IntroTemplate")),
            new FileReference(Path.Combine("C:", "PrintableBook", "brands", "Demo", "IntroTemplate", "nested", "intro.png")));

        Assert.Equal("nested/intro.png", key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../intro.png")]
    [InlineData("nested/../intro.png")]
    public void Normalize_rejects_nonportable_or_traversal_keys(string value) =>
        Assert.Throws<ArgumentException>(() => IntroTemplateSourceKey.Normalize(value));

    [Fact]
    public void Normalize_rejects_a_rooted_key() =>
        Assert.Throws<ArgumentException>(() => IntroTemplateSourceKey.Normalize(Path.Combine("C:", "intro.png")));
}
