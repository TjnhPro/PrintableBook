using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class InteriorSourceKeyTests
{
    [Fact]
    public void FromBookRoot_normalizes_a_relative_source_path()
    {
        var key = InteriorSourceKey.FromBookRoot(new DirectoryReference("C:\\Books\\Book-001"), new FileReference("C:\\Books\\Book-001\\Book interior\\page-007.png"));
        Assert.Equal("Book interior/page-007.png", key);
    }

    [Fact]
    public void FromBookRoot_rejects_a_source_outside_the_book()
    {
        Assert.Throws<ArgumentException>(() => InteriorSourceKey.FromBookRoot(new DirectoryReference("C:\\Books\\Book-001"), new FileReference("C:\\Books\\other.png")));
    }
}
