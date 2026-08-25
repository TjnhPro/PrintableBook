using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class BookProcessingProgressTests
{
    [Theory]
    [InlineData("", 0, 1)]
    [InlineData("scan", -1, 1)]
    [InlineData("scan", 0, 0)]
    [InlineData("scan", 2, 1)]
    public void Constructor_rejects_invalid_progress(string step, int completed, int total)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BookProcessingProgress(new BookId("book"), BookProcessingStatus.Running, step, completed, total));
    }
}
