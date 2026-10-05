using PrintableBook.Core.Application.Brands;

namespace PrintableBook.Core.Tests.Application.Brands;

public sealed class SupportedLanguageCatalogTests
{
    [Fact]
    public void Catalog_exposes_the_seven_canonical_languages_in_stable_order()
    {
        Assert.Collection(
            SupportedLanguageCatalog.All,
            option => Assert.Equal(new SupportedLanguageOption("en", "English"), option),
            option => Assert.Equal(new SupportedLanguageOption("de", "German"), option),
            option => Assert.Equal(new SupportedLanguageOption("fr", "French"), option),
            option => Assert.Equal(new SupportedLanguageOption("es", "Spanish"), option),
            option => Assert.Equal(new SupportedLanguageOption("it", "Italian"), option),
            option => Assert.Equal(new SupportedLanguageOption("pt", "Portuguese"), option),
            option => Assert.Equal(new SupportedLanguageOption("nl", "Dutch"), option));
    }

    [Fact]
    public void TryGet_trims_and_canonicalizes_a_supported_code()
    {
        Assert.True(SupportedLanguageCatalog.TryGet(" FR ", out var language));
        Assert.Equal(new SupportedLanguageOption("fr", "French"), language);
    }

    [Theory]
    [InlineData("ColoringWorld", "de", "ColoringWorld_de")]
    [InlineData("ColoringWorld_de", "fr", "ColoringWorld_fr")]
    [InlineData("ColoringWorld_de_fr", "en", "ColoringWorld_de_en")]
    [InlineData("ColoringWorld_custom", "de", "ColoringWorld_custom_de")]
    public void Naming_appends_or_replaces_at_most_one_recognized_suffix(string source, string languageCode, string expected)
    {
        Assert.True(SupportedLanguageCatalog.TryGet(languageCode, out var language));

        Assert.Equal(expected, BrandCloneNamingPolicy.CreateDestinationName(source, language));
    }
}
