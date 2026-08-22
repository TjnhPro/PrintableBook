using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Tests.Abstractions;

public sealed class TechnicalPrimitivesTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void ImageSize_rejects_non_positive_dimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageSize(width, height));
    }

    [Fact]
    public void ImageRectangle_requires_bounds_large_enough_to_represent_an_area()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageRectangle(new ImagePoint(4, 5), new ImageSize(0, 10)));
    }

    [Fact]
    public void FileReference_rejects_a_blank_reference()
    {
        Assert.Throws<ArgumentException>(() => new FileReference(" "));
    }
}
