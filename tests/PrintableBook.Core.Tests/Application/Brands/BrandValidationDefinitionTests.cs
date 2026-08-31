using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Tests.Application.Brands;

public sealed class BrandValidationDefinitionTests
{
    [Fact]
    public void Current_tracks_only_intro_frame_and_background()
    {
        var definition = BrandValidationDefinition.CreateCurrent(GlobalSettings.Default);

        Assert.Equal(["intro", "frame", "background"], definition.Entries.Select(entry => entry.Key));
        Assert.DoesNotContain(definition.Entries, entry => entry.Target.RelativePath.Contains("AppPlus", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(definition.Entries, entry => entry.Target.RelativePath.Contains("BackCover", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(definition.Entries, entry => entry.Target.RelativePath.Contains("brand.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Current_requires_at_least_one_supported_intro_image()
    {
        var definition = BrandValidationDefinition.CreateCurrent(GlobalSettings.Default);
        var intro = Assert.IsType<BrandValidationDirectoryFilesTarget>(
            Assert.Single(definition.Entries, entry => entry.Key == "intro").Target);

        Assert.Equal("IntroTemplate", intro.RelativePath);
        Assert.True(intro.Recursive);
        Assert.Equal(1, intro.MinimumFileCount);
        Assert.True(intro.Extensions.SetEquals([".png", ".jpg", ".jpeg"]));
    }

    [Fact]
    public void Current_uses_existing_processing_dimension_contracts()
    {
        var settings = GlobalSettings.Default with
        {
            ArtworkMaximumSide = 2000,
            FinalPageWidth = 2500,
            FinalPageHeight = 2600
        };

        var definition = BrandValidationDefinition.CreateCurrent(settings);

        Assert.Equal([new ImageSize(1024, 1024), new ImageSize(2048, 2048)], GetDimensions(definition, "intro"));
        Assert.Equal([new ImageSize(2000, 2000)], GetDimensions(definition, "frame"));
        Assert.Equal([new ImageSize(2500, 2600)], GetDimensions(definition, "background"));
    }

    [Fact]
    public void Definition_changed_at_is_the_locked_utc_timestamp()
    {
        var definition = BrandValidationDefinition.CreateCurrent(GlobalSettings.Default);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 31, 4, 32, 0, TimeSpan.Zero),
            definition.DefinitionChangedAtUtc);
    }

    private static IReadOnlyList<ImageSize> GetDimensions(BrandValidationDefinition definition, string key) =>
        Assert.IsType<BrandImageDimensionsRule>(
            Assert.Single(Assert.Single(definition.Entries, entry => entry.Key == key).Rules.OfType<BrandImageDimensionsRule>())).AllowedSizes;
}
