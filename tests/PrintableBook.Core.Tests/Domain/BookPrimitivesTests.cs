using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Domain;

public sealed class BookPrimitivesTests
{
    [Fact]
    public void BookId_rejects_a_blank_value()
    {
        var exception = Assert.Throws<ArgumentException>(() => new BookId("   "));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Book_source_groups_assets_by_their_explicit_kind()
    {
        var source = new BookSource(
            new[]
            {
                new BookAsset("cover.png", BookAssetKind.Cover),
                new BookAsset("page-001.png", BookAssetKind.Interior),
                new BookAsset("page-001-colored.png", BookAssetKind.Colored)
            });

        Assert.Single(source.GetAssets(BookAssetKind.Cover));
        Assert.Single(source.GetAssets(BookAssetKind.Interior));
        Assert.Single(source.GetAssets(BookAssetKind.Colored));
        Assert.Empty(source.GetAssets(BookAssetKind.Intro));
    }
}
