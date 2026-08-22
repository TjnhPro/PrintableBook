namespace PrintableBook.Core.Abstractions;

public readonly record struct PdfDocumentInfo
{
    public PdfDocumentInfo(int pageCount)
    {
        if (pageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), "PDF page count cannot be negative.");
        }

        PageCount = pageCount;
    }

    public int PageCount { get; }
}
