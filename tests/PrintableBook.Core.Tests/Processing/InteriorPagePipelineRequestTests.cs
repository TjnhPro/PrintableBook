using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Processing;

public sealed class InteriorPagePipelineRequestTests
{
    [Fact]
    public void Constructor_accepts_the_canonical_prepared_working_and_final_geometry()
    {
        var request = CreateRequest(new ImageSize(2270, 2270), new ImageSize(2550, 2550), new ImageSize(2588, 2625));

        Assert.Equal(new ImageSize(2270, 2270), request.PreparedArtworkSize);
        Assert.Equal(new ImageSize(2550, 2550), request.WorkingPageSize);
        Assert.Equal(new ImageSize(2588, 2625), request.FinalPageSize);
    }

    [Fact]
    public void Constructor_rejects_prepared_artwork_larger_than_the_working_page() =>
        Assert.Throws<ArgumentException>(() => CreateRequest(
            new ImageSize(2551, 2270), new ImageSize(2550, 2550), new ImageSize(2588, 2625)));

    [Fact]
    public void Constructor_rejects_working_page_larger_than_the_final_page() =>
        Assert.Throws<ArgumentException>(() => CreateRequest(
            new ImageSize(2270, 2270), new ImageSize(2589, 2550), new ImageSize(2588, 2625)));

    private static InteriorPagePipelineRequest CreateRequest(ImageSize prepared, ImageSize working, ImageSize final) => new(
        new BookWorkspace(new BookId("book"), new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("output")),
        new FileReference("source.png"),
        "page-01",
        new ArtworkDetectionThreshold(20),
        prepared,
        working,
        final,
        new ImageDensity(300, 300),
        null,
        false);
}
