using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class ClassificationAlgorithmVersionTests
{
    [Fact]
    public void Current_is_the_stable_v1_classification_contract()
    {
        Assert.Equal("artwork-classification-v1", ClassificationAlgorithmVersion.Current);
    }
}
