namespace PrintableBook.Core.Domain.Books;

public sealed class BookSource
{
    private readonly IReadOnlyList<BookAsset> assets;

    public BookSource(IEnumerable<BookAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        this.assets = assets.ToArray();
    }

    public IReadOnlyList<BookAsset> Assets => assets;

    public IReadOnlyList<BookAsset> GetAssets(BookAssetKind kind) =>
        assets.Where(asset => asset.Kind == kind).ToArray();
}
