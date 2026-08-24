using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class ArtworkPreparationAlgorithmVersionTests
{
    [Fact]
    public void Current_identifies_the_certified_preparation_semantics() =>
        Assert.Equal("artwork-preparation-v1", ArtworkPreparationAlgorithmVersion.Current);
}
