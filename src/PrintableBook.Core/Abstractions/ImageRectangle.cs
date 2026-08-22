namespace PrintableBook.Core.Abstractions;

public readonly record struct ImageRectangle
{
    public ImageRectangle(ImagePoint origin, ImageSize size)
    {
        Origin = origin;
        Size = size;
    }

    public ImagePoint Origin { get; }

    public ImageSize Size { get; }
}
