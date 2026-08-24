using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Infrastructure.Discovery;
using PrintableBook.Infrastructure.FileSystem;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PhysicalBrandFrameResolverTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BrandFrameResolver.{Guid.NewGuid():N}");

    [Fact]
    public async Task ResolveCompatibleFrameAsync_returns_only_a_frame_matching_the_requested_page_size()
    {
        Directory.CreateDirectory(rootPath);
        var framePath = Path.Combine(rootPath, "frame.png");
        await File.WriteAllTextAsync(framePath, "fixture");
        var brand = new DiscoveredBrand("Demo", new DirectoryReference(rootPath));
        var resolver = new PhysicalBrandFrameResolver(new PhysicalFileSystem(), new StubImageInspector(new ImageSize(2588, 2625)));

        var compatible = await resolver.ResolveCompatibleFrameAsync(brand, new ImageSize(2588, 2625));
        var incompatible = await resolver.ResolveCompatibleFrameAsync(brand, new ImageSize(2588, 2588));

        Assert.Equal(framePath, compatible!.Value);
        Assert.Null(incompatible);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private sealed class StubImageInspector(ImageSize size) : IImageInspector
    {
        public ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default) => ValueTask.FromResult(size);

        public ValueTask<ImageInfo> GetInfoAsync(FileReference image, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ImageInfo(size, null));
    }
}
