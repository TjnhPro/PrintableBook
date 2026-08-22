using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class MagickCoverValidatorTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.CoverTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ValidateAsync_accepts_a_readable_cover_that_meets_both_minimum_dimensions()
    {
        Directory.CreateDirectory(rootPath);
        var cover = Path.Combine(rootPath, "cover.png");
        using (var image = new MagickImage(MagickColors.White, 400, 500))
        {
            image.Write(cover);
        }

        var result = await new MagickCoverValidator().ValidateAsync(new CoverValidationRequest(
            new FileReference(cover),
            new ImageSize(400, 500)));

        Assert.True(result.IsValid);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_undersized_or_corrupt_cover_without_changing_the_source()
    {
        Directory.CreateDirectory(rootPath);
        var undersized = Path.Combine(rootPath, "undersized.png");
        using (var image = new MagickImage(MagickColors.White, 399, 500))
        {
            image.Write(undersized);
        }
        var lengthBeforeValidation = new FileInfo(undersized).Length;

        var validator = new MagickCoverValidator();
        var undersizedResult = await validator.ValidateAsync(new CoverValidationRequest(
            new FileReference(undersized),
            new ImageSize(400, 500)));
        var corruptFile = Path.Combine(rootPath, "corrupt.png");
        await File.WriteAllTextAsync(corruptFile, "not a png");
        var corruptResult = await validator.ValidateAsync(new CoverValidationRequest(
            new FileReference(corruptFile),
            new ImageSize(400, 500)));

        Assert.False(undersizedResult.IsValid);
        Assert.Equal("cover.resolution_too_small", undersizedResult.Failure!.Code);
        Assert.Equal(lengthBeforeValidation, new FileInfo(undersized).Length);
        Assert.False(corruptResult.IsValid);
        Assert.Equal("cover.unreadable", corruptResult.Failure!.Code);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }
}
