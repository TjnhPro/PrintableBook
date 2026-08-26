using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Application;

public sealed class BookSourceValidatorTests
{
    [Fact]
    public void Validate_keeps_supported_image_extensions_and_preserves_asset_kinds_in_order()
    {
        var source = new BookSource([
            new BookAsset("Cover/cover.png", BookAssetKind.Cover),
            new BookAsset("Interior/page-001.jpg", BookAssetKind.Interior),
            new BookAsset("Interior/page-002.jpeg", BookAssetKind.Interior),
            new BookAsset("Interior/notes.txt", BookAssetKind.Interior),
            new BookAsset("Colored/preview.PNG", BookAssetKind.Colored)]);

        var result = BookSourceValidator.Validate(source);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Cover/cover.png", "Interior/page-001.jpg", "Interior/page-002.jpeg", "Colored/preview.PNG"],
            result.Source.Assets.Select(asset => asset.Reference));
        Assert.Equal(BookAssetKind.Interior, result.Source.GetAssets(BookAssetKind.Interior)[0].Kind);
    }

    [Fact]
    public void Validate_returns_interior_empty_when_only_unsupported_interior_files_were_discovered()
    {
        var source = new BookSource([
            new BookAsset("Interior/notes.txt", BookAssetKind.Interior),
            new BookAsset("Interior/page.webp", BookAssetKind.Interior),
            new BookAsset("Cover/cover.png", BookAssetKind.Cover)]);

        var result = BookSourceValidator.Validate(source);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.interior_empty", result.Failure!.Code);
        Assert.Equal("Cover/cover.png", Assert.Single(result.Source.Assets).Reference);
    }

    [Fact]
    public void Validate_returns_interior_empty_when_no_interior_files_were_discovered()
    {
        var source = new BookSource([new BookAsset("Cover/cover.png", BookAssetKind.Cover)]);

        var result = BookSourceValidator.Validate(source);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.interior_empty", result.Failure!.Code);
    }
}
