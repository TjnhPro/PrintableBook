using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Infrastructure.BrandValidation;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;

namespace PrintableBook.Infrastructure.Tests;

public sealed class BrandValidationLifecycleIntegrationTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BrandValidation.{Guid.NewGuid():N}");

    [Fact]
    public async Task ValidateAsync_persists_a_real_certificate_and_check_state_uses_only_metadata_for_the_tracked_scope()
    {
        var brandPath = Path.Combine(rootPath, "demo");
        var introPath = Path.Combine(brandPath, "IntroTemplate", "nested", "intro.png");
        var framePath = Path.Combine(brandPath, "frame.png");
        var backgroundPath = Path.Combine(brandPath, "background.png");
        Directory.CreateDirectory(Path.GetDirectoryName(introPath)!);
        var settings = GlobalSettings.Default with { ArtworkMaximumSide = 64, FinalPageWidth = 80, FinalPageHeight = 90 };
        WriteImage(introPath, 1024, 1024);
        WriteImage(framePath, 64, 64);
        WriteImage(backgroundPath, 80, 90);

        var fileSystem = new PhysicalFileSystem();
        var images = new CountingImageInspector(new MagickImageInspector());
        var resolver = new BrandValidationTargetResolver(fileSystem);
        var service = new BrandValidationService(
            new JsonBrandValidationStateStore(fileSystem),
            resolver,
            new BrandFingerprintCalculator(fileSystem, resolver),
            fileSystem,
            images);
        var brandDirectory = new DirectoryReference(brandPath);

        var result = await service.ValidateAsync(brandDirectory, settings);
        var imageReadsAfterValidation = images.SizeReads;
        await File.WriteAllTextAsync(Path.Combine(brandPath, "brand.json"), "{ }");
        var certified = await service.CheckStateAsync(brandDirectory, settings);

        Assert.True(result.IsSuccess, string.Join("; ", result.Failures.Select(failure => failure.Message)));
        Assert.True(File.Exists(Path.Combine(brandPath, "brand.validation.json")));
        Assert.Equal(3, imageReadsAfterValidation);
        Assert.Equal(BrandValidationStatus.Validated, certified.Status);
        Assert.Equal(imageReadsAfterValidation, images.SizeReads);

        File.SetLastWriteTimeUtc(framePath, DateTime.UtcNow.AddMinutes(2));
        var stale = await service.CheckStateAsync(brandDirectory, settings);

        Assert.Equal(BrandValidationStatus.NeedsValidation, stale.Status);
        Assert.Equal("brand_fingerprint_changed", stale.ReasonCode);
        Assert.Equal(imageReadsAfterValidation, images.SizeReads);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private static void WriteImage(string path, uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.White, width, height);
        image.Write(path);
    }

    private sealed class CountingImageInspector(IImageInspector inner) : IImageInspector
    {
        public int SizeReads { get; private set; }

        public async ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default)
        {
            SizeReads++;
            return await inner.GetSizeAsync(image, cancellationToken);
        }

        public ValueTask<ImageInfo> GetInfoAsync(FileReference image, CancellationToken cancellationToken = default) =>
            inner.GetInfoAsync(image, cancellationToken);
    }
}
