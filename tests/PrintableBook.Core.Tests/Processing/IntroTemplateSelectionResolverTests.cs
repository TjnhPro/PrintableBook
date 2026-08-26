using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class IntroTemplateSelectionResolverTests
{
    [Fact]
    public void Automatic_selection_uses_only_supported_images_in_filename_order()
    {
        var result = IntroTemplateSelectionResolver.Resolve(false, null,
        [Asset("z.jpg"), Asset("ignore.psd"), Asset("a.png")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(["a.png", "z.jpg"], result.Assets.Select(asset => asset.Key));
    }

    [Fact]
    public void Automatic_selection_reports_when_no_supported_templates_exist()
    {
        var result = IntroTemplateSelectionResolver.Resolve(false, null, [Asset("ignore.psd")]);

        Assert.Equal("intro.template_empty", result.Failure!.Code);
    }

    [Fact]
    public void Custom_selection_preserves_user_order_and_matches_keys_case_insensitively()
    {
        var result = IntroTemplateSelectionResolver.Resolve(true, ["SECOND.JPG", "first.png"], [Asset("first.png"), Asset("second.jpg")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(["second.jpg", "first.png"], result.Assets.Select(asset => asset.Key));
    }

    [Fact]
    public void Custom_selection_requires_a_choice_and_reports_missing_assets()
    {
        Assert.Equal("intro.selection_required", IntroTemplateSelectionResolver.Resolve(true, [], [Asset("first.png")]).Failure!.Code);
        Assert.Equal("intro.selection_missing", IntroTemplateSelectionResolver.Resolve(true, ["gone.png"], [Asset("first.png")]).Failure!.Code);
    }

    private static DiscoveredIntroTemplateAsset Asset(string key) =>
        new(key, Path.Combine("C:", "brand", "IntroTemplate", key), key, new Uri(Path.Combine("C:", "brand", "IntroTemplate", key)).AbsoluteUri);
}
