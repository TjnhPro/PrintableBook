using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class PreparedArtworkTests
{
    [Theory]
    [InlineData(ArtworkType.BorderArt, true)]
    [InlineData(ArtworkType.FullArt, true)]
    [InlineData(ArtworkType.CropArt, false)]
    public void Result_carries_the_prepared_file_type_and_frame_eligibility(ArtworkType type, bool frameAllowed)
    {
        var file = new FileReference("prepared.png");

        var result = new PreparedArtwork(file, type, frameAllowed);

        Assert.Equal(file, result.File);
        Assert.Equal(type, result.Type);
        Assert.Equal(frameAllowed, result.FrameAllowed);
    }

    [Theory]
    [InlineData(ArtworkType.BorderArt, true)]
    [InlineData(ArtworkType.FullArt, true)]
    [InlineData(ArtworkType.CropArt, false)]
    public void FromCached_restores_the_type_owned_frame_policy(ArtworkType type, bool frameAllowed)
    {
        var file = new FileReference("prepared.png");

        var result = PreparedArtwork.FromCached(file, type);

        Assert.Equal(file, result.File);
        Assert.Equal(type, result.Type);
        Assert.Equal(frameAllowed, result.FrameAllowed);
    }
}
