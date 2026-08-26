using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Processing;

public sealed class InteriorPagePipelineRequestTests
{
    [Fact]
    public void Request_carries_a_page_role_and_rejects_unknown_roles()
    {
        var intro = new InteriorPagePipelineRequest(new BookWorkspace(new BookId("book"), new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("temp")), new FileReference("intro.png"), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(100, 100), new ImageSize(100, 100), new ImageSize(100, 100), new ImageDensity(300, 300), null, FrameMode.Disabled, processingKind: InteriorPageProcessingKind.IntroTemplate);

        Assert.Equal(InteriorPageProcessingKind.IntroTemplate, intro.ProcessingKind);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InteriorPagePipelineRequest(new BookWorkspace(new BookId("book"), new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("temp")), new FileReference("intro.png"), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(100, 100), new ImageSize(100, 100), new ImageSize(100, 100), new ImageDensity(300, 300), null, FrameMode.Disabled, processingKind: (InteriorPageProcessingKind)99));
    }

    [Fact]
    public void Intro_template_request_rejects_every_frame_configuration()
    {
        var workspace = new BookWorkspace(new BookId("book"), new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("temp"));

        Assert.Throws<ArgumentException>(() => new InteriorPagePipelineRequest(workspace, new FileReference("intro.png"), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(100, 100), new ImageSize(100, 100), new ImageSize(100, 100), new ImageDensity(300, 300), new FileReference("frame.png"), FrameMode.Disabled, processingKind: InteriorPageProcessingKind.IntroTemplate));
        Assert.Throws<ArgumentException>(() => new InteriorPagePipelineRequest(workspace, new FileReference("intro.png"), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(100, 100), new ImageSize(100, 100), new ImageSize(100, 100), new ImageDensity(300, 300), null, FrameMode.Auto, processingKind: InteriorPageProcessingKind.IntroTemplate));
    }

    [Fact]
    public void Constructor_accepts_the_canonical_prepared_working_and_final_geometry()
    {
        var request = CreateRequest(new ImageSize(2270, 2270), new ImageSize(2550, 2550), new ImageSize(2588, 2625));

        Assert.Equal(new ImageSize(2270, 2270), request.PreparedArtworkSize);
        Assert.Equal(new ImageSize(2550, 2550), request.WorkingPageSize);
        Assert.Equal(new ImageSize(2588, 2625), request.FinalPageSize);
        Assert.Equal(FrameMode.Auto, request.FrameMode);
    }

    [Theory]
    [InlineData(FrameMode.Auto)]
    [InlineData(FrameMode.Enabled)]
    [InlineData(FrameMode.Disabled)]
    public void Constructor_preserves_the_page_frame_mode(FrameMode mode)
    {
        var request = CreateRequest(new ImageSize(2270, 2270), new ImageSize(2550, 2550), new ImageSize(2588, 2625), mode);

        Assert.Equal(mode, request.FrameMode);
    }

    [Fact]
    public void Constructor_rejects_prepared_artwork_larger_than_the_working_page() =>
        Assert.Throws<ArgumentException>(() => CreateRequest(
            new ImageSize(2551, 2270), new ImageSize(2550, 2550), new ImageSize(2588, 2625)));

    [Fact]
    public void Constructor_rejects_working_page_larger_than_the_final_page() =>
        Assert.Throws<ArgumentException>(() => CreateRequest(
            new ImageSize(2270, 2270), new ImageSize(2589, 2550), new ImageSize(2588, 2625)));

    private static InteriorPagePipelineRequest CreateRequest(ImageSize prepared, ImageSize working, ImageSize final, FrameMode mode = FrameMode.Auto) => new(
        new BookWorkspace(new BookId("book"), new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("output")),
        new FileReference("source.png"),
        "page-01",
        new ArtworkDetectionThreshold(20),
        prepared,
        working,
        final,
        new ImageDensity(300, 300),
        null,
        mode);
}
