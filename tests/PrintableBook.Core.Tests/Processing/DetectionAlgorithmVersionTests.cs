using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class DetectionAlgorithmVersionTests
{
    [Fact]
    public void Versions_identify_the_canonical_v3_processing_contract()
    {
        Assert.Equal("artwork-source-normalization-v1", ArtworkSourceNormalizationAlgorithmVersion.Current);
        Assert.Equal("borderline-v3", BorderLineAlgorithmVersion.Current);
        Assert.Equal("artwork-classification-v2", ClassificationAlgorithmVersion.Current);
    }
}
