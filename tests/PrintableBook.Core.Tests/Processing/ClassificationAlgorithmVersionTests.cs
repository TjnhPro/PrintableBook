using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class ClassificationAlgorithmVersionTests
{
    [Fact]
    public void Current_is_the_canonical_v2_classification_contract()
    {
        Assert.Equal("artwork-classification-v2", ClassificationAlgorithmVersion.Current);
    }
}
