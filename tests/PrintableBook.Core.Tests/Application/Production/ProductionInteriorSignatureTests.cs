using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Production;

namespace PrintableBook.Core.Tests.Application.Production;

public sealed class ProductionInteriorSignatureTests
{
    [Fact]
    public void Signature_changes_for_every_output_affecting_recipe_class()
    {
        var baseline = Recipe();
        var signature = ProductionInteriorSignature.Create(baseline);

        Assert.True(ProductionInteriorSignature.IsCurrent(signature));
        Assert.NotEqual(signature, ProductionInteriorSignature.Create(baseline with { RenderingSignature = "rendering-b" }));
        Assert.NotEqual(signature, ProductionInteriorSignature.Create(baseline with { IntroPages = [Fact("intro", "intro-b.png", 11)] }));
        Assert.NotEqual(signature, ProductionInteriorSignature.Create(baseline with
        {
            InteriorPages = [new ProductionInteriorPageFact("Book interior/page.png", Fact("interior", "page.png", 12), FrameMode.Enabled)],
            Frame = Fact("frame", "frame.png", 13)
        }));
        Assert.NotEqual(signature, ProductionInteriorSignature.Create(baseline with { BackgroundEnabled = false, Background = null }));
        Assert.NotEqual(signature, ProductionInteriorSignature.Create(baseline with
        {
            ShuffleMap = new InteriorShuffleMap([new InteriorShuffleEntry(new FileReference("page.png"), 1)], 12)
        }));
        Assert.NotEqual(signature, ProductionInteriorSignature.Create(baseline with { ProductionPrefixPages = [Fact("prefix", "owner.png", 14)] }));
    }

    [Fact]
    public void Signature_ignores_object_identity_and_is_deterministic()
    {
        Assert.Equal(
            ProductionInteriorSignature.Create(Recipe()),
            ProductionInteriorSignature.Create(Recipe()));
        Assert.False(ProductionInteriorSignature.IsCurrent("sha256:legacy"));
    }

    private static ProductionInteriorSignatureRecipe Recipe() => new(
        "rendering-a",
        [Fact("prefix", "cover.png", 1), Fact("prefix", "owner.png", 2)],
        [Fact("intro", "intro.png", 3)],
        [new ProductionInteriorPageFact("Book interior/page.png", Fact("interior", "page.png", 4), FrameMode.Disabled)],
        new InteriorShuffleMap([new InteriorShuffleEntry(new FileReference("page.png"), 1)], 11),
        null,
        true,
        Fact("background", "background.png", 5));

    private static ProductionInteriorFileFact Fact(string role, string identity, int version) =>
        new(role, identity, new ProductionFileSignature(version, DateTimeOffset.UnixEpoch.AddMinutes(version)));
}
