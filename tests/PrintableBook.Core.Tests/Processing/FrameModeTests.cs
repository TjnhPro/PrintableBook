using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Tests.Processing;

public sealed class FrameModeTests
{
    [Fact]
    public void Auto_is_the_default_and_only_supported_frame_mode()
    {
        Assert.Equal(FrameMode.Auto, default);
        Assert.Equal([FrameMode.Auto, FrameMode.Enabled, FrameMode.Disabled], Enum.GetValues<FrameMode>());
    }
}
