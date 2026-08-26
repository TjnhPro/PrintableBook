using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class IntroTemplateSelectionResolverTests
{
    [Fact]
    public void Automatic_selection_uses_only_supported_images_in_filename_order()
    {
        var result = IntroTemplateSelectionResolver.Resolve(
        [Asset("z.jpg"), Asset("ignore.psd"), Asset("a.png")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(["a.png", "z.jpg"], result.Assets.Select(asset => asset.Key));
    }

    [Fact]
    public void Automatic_selection_reports_when_no_supported_templates_exist()
    {
        var result = IntroTemplateSelectionResolver.Resolve([Asset("ignore.psd")]);

        Assert.Equal("intro.template_empty", result.Failure!.Code);
    }

    private static DiscoveredIntroTemplateAsset Asset(string key) =>
        new(key, Path.Combine("C:", "brand", "IntroTemplate", key), key, new Uri(Path.Combine("C:", "brand", "IntroTemplate", key)).AbsoluteUri);
}
