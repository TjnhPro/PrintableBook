using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Scanning;

public static class BookSourceValidator
{
    public static BookSourceValidationResult Validate(BookSource discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        var eligible = new BookSource(discovered.Assets.Where(asset => BookSourceLayout.IsSupportedImage(asset.Reference)));
        if (eligible.GetAssets(BookAssetKind.Interior).Count == 0)
        {
            return BookSourceValidationResult.Failed(
                eligible,
                new ProcessingFailure(
                    "book.interior_empty",
                    "The book does not contain any readable interior images."));
        }

        return BookSourceValidationResult.Succeeded(eligible);
    }
}
