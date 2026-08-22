using PrintableBook.Core.Configuration;
using PrintableBook.Core.Domain.Brands;

namespace PrintableBook.Core.Tests.Domain;

public sealed class BrandProfileTests
{
    [Fact]
    public void BrandId_rejects_a_blank_value()
    {
        var exception = Assert.Throws<ArgumentException>(() => new BrandId(" "));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void BrandProfile_allows_missing_optional_configuration_and_assets()
    {
        var profile = new BrandProfile(new BrandId("publisher-one"));

        Assert.Null(profile.Settings);
        Assert.Empty(profile.Resources);
    }

    [Fact]
    public void BrandProfile_keeps_the_resolved_settings_as_an_opaque_snapshot()
    {
        var settings = new EffectiveProcessingSettings(new Dictionary<string, string?>
        {
            ["frame.choice"] = "soft-line"
        });

        var profile = new BrandProfile(new BrandId("publisher-one"), settings);

        Assert.Equal("soft-line", profile.Settings!["frame.choice"]);
    }
}
