namespace PrintableBook.Core.Domain.Books;

/// <summary>
/// Names the source folders the application can inspect without treating every
/// discovered folder as a processing requirement.
/// </summary>
public static class BookSourceLayout
{
    public static IReadOnlyList<string> KnownFolderNames { get; } =
    [
        "Book colored",
        "Book cover",
        "Book interior",
        "Source cover",
        "Source cover colored"
    ];

    public static IReadOnlyList<(string Directory, BookAssetKind Kind)> ProcessingFolders { get; } =
    [
        ("Cover", BookAssetKind.Cover),
        ("Intro", BookAssetKind.Intro),
        ("Interior", BookAssetKind.Interior),
        ("Colored", BookAssetKind.Colored),
        ("Source cover", BookAssetKind.Cover),
        ("Book interior", BookAssetKind.Interior),
        ("Book colored", BookAssetKind.Colored)
    ];

    public static bool IsSupportedImage(string path) =>
        Path.GetExtension(path) is ".png" or ".PNG" or ".jpg" or ".JPG" or ".jpeg" or ".JPEG";
}
