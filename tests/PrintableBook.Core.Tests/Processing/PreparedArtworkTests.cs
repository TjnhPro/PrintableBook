using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class PreparedArtworkTests
{
    [Theory]
    [InlineData(ArtworkType.BorderArt, true)]
    [InlineData(ArtworkType.FullArt, true)]
    [InlineData(ArtworkType.CropArt, false)]
    public void Result_carries_the_prepared_file_type_and_auto_frame_recommendation(ArtworkType type, bool autoFrameRecommended)
    {
        var file = new FileReference("prepared.png");

        var result = new PreparedArtwork(file, type, autoFrameRecommended);

        Assert.Equal(file, result.File);
        Assert.Equal(type, result.Type);
        Assert.Equal(autoFrameRecommended, result.AutoFrameRecommended);
    }

    [Theory]
    [InlineData(ArtworkType.BorderArt, true)]
    [InlineData(ArtworkType.FullArt, true)]
    [InlineData(ArtworkType.CropArt, false)]
    public void FromCached_restores_the_type_owned_auto_frame_recommendation(ArtworkType type, bool autoFrameRecommended)
    {
        var file = new FileReference("prepared.png");

        var result = PreparedArtwork.FromCached(file, type);

        Assert.Equal(file, result.File);
        Assert.Equal(type, result.Type);
        Assert.Equal(autoFrameRecommended, result.AutoFrameRecommended);
    }
}
