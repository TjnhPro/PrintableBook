namespace PrintableBook.Core.Domain.Books;

public sealed record BookAsset
{
    public BookAsset(string reference, BookAssetKind kind)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("An asset reference is required.", nameof(reference));
        }

        Reference = reference;
        Kind = kind;
    }

    public string Reference { get; }

    public BookAssetKind Kind { get; }
}
