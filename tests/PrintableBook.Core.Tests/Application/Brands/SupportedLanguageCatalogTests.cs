using PrintableBook.Core.Application.Brands;

namespace PrintableBook.Core.Tests.Application.Brands;

public sealed class SupportedLanguageCatalogTests
{
    [Fact]
    public void Catalog_exposes_the_eight_canonical_languages_in_stable_order()
    {
        Assert.Collection(
            SupportedLanguageCatalog.All,
            option => Assert.Equal(new SupportedLanguageOption("en", "English"), option),
            option => Assert.Equal(new SupportedLanguageOption("de", "German"), option),
            option => Assert.Equal(new SupportedLanguageOption("fr", "French"), option),
            option => Assert.Equal(new SupportedLanguageOption("es", "Spanish"), option),
            option => Assert.Equal(new SupportedLanguageOption("it", "Italian"), option),
            option => Assert.Equal(new SupportedLanguageOption("pt", "Portuguese"), option),
            option => Assert.Equal(new SupportedLanguageOption("ja", "Japanese"), option),
            option => Assert.Equal(new SupportedLanguageOption("nl", "Dutch"), option));
    }

    [Fact]
    public void TryGet_trims_and_canonicalizes_a_supported_code()
    {
        Assert.True(SupportedLanguageCatalog.TryGet(" FR ", out var language));
        Assert.Equal(new SupportedLanguageOption("fr", "French"), language);
    }

    [Fact]
    public void Effective_language_defaults_missing_values_to_English_and_rejects_unknown_codes()
    {
        Assert.Equal(new SupportedLanguageOption("en", "English"), SupportedLanguageCatalog.GetEffective(null));
        Assert.Equal("en", BrandMetadata.Create("Jane").EffectiveLanguage.Code);
        Assert.Equal("de", SupportedLanguageCatalog.NormalizeStoredCode(" DE "));
        Assert.Throws<ArgumentException>(() => SupportedLanguageCatalog.GetEffective("xx"));
    }

    [Fact]
    public void Legacy_language_does_not_come_from_a_recognized_name_suffix()
    {
        var legacyMetadata = BrandMetadata.Create("Jane");

        Assert.Equal("en", legacyMetadata.EffectiveLanguage.Code);
        Assert.Equal("AnimalBook_fr", LanguageEditionNamingPolicy.CreateDestinationName(
            "AnimalBook_de",
            SupportedLanguageCatalog.All.Single(language => language.Code == "fr")));
    }

    [Theory]
    [InlineData("ColoringWorld", "de", "ColoringWorld_de")]
    [InlineData("ColoringWorld_de", "fr", "ColoringWorld_fr")]
    [InlineData("ColoringWorld_de_fr", "en", "ColoringWorld_de_en")]
    [InlineData("ColoringWorld_custom", "de", "ColoringWorld_custom_de")]
    public void Naming_appends_or_replaces_at_most_one_recognized_suffix(string source, string languageCode, string expected)
    {
        Assert.True(SupportedLanguageCatalog.TryGet(languageCode, out var language));

        Assert.Equal(expected, LanguageEditionNamingPolicy.CreateDestinationName(source, language));
    }
}
