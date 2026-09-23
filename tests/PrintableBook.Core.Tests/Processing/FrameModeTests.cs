using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class FrameModeTests
{
    [Fact]
    public void No_frame_is_the_default_and_only_two_modes_are_supported()
    {
        Assert.Equal(FrameMode.Disabled, default);
        Assert.Equal([FrameMode.Disabled, FrameMode.Enabled], Enum.GetValues<FrameMode>());
    }
}
