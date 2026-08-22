using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class InteriorShuffleIndexGeneratorTests
{
    [Fact]
    public void Generate_with_the_same_seed_produces_the_same_complete_permutation()
    {
        var pages = new[]
        {
            new FileReference("page-01.png"),
            new FileReference("page-02.png"),
            new FileReference("page-03.png"),
            new FileReference("page-04.png")
        };

        var first = InteriorShuffleIndexGenerator.Generate(pages, seed: 42);
        var second = InteriorShuffleIndexGenerator.Generate(pages, seed: 42);

        Assert.Equal(first.Entries, second.Entries);
        Assert.Equal([1, 2, 3, 4], first.Entries.Select(entry => entry.OutputIndex).Order());
        Assert.Equal(pages.OrderBy(page => page.Value), first.Entries.Select(entry => entry.Page).OrderBy(page => page.Value));
    }
}
