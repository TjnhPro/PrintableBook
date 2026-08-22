using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class DiskBackedInteriorPagePipelineTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.PagePipelineTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ProcessAsync_writes_each_real_stage_to_cache_then_reopens_the_final_png()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "source.png");
        using (var image = new MagickImage(MagickColors.White, 80, 100))
        {
            image.GetPixels().SetPixel(20, 20, [0, 0, 0]);
            image.GetPixels().SetPixel(59, 79, [0, 0, 0]);
            image.Write(source);
        }

        var fileSystem = new PhysicalFileSystem();
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "Book"));
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(new BookId("book-one"), bookDirectory);
        var pipeline = new DiskBackedInteriorPagePipeline(
            new MagickArtworkTrimProcessor(),
            new MagickSquareCanvasProcessor(),
            new MagickArtworkResizeProcessor(),
            new MagickFrameProcessor(),
            new MagickFinalInteriorPageProcessor());

        var result = await pipeline.ProcessAsync(new InteriorPagePipelineRequest(
            workspace,
            new FileReference(source),
            "page-01",
            new ArtworkDetectionThreshold(20),
            new ImageSize(200, 200),
            new ImageDensity(300, 300),
            null,
            false));

        Assert.Equal("page-01", result.PageId);
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "trim.png")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "canvas.png")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "resize.png")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "frame.png")));
        var finalInfo = await new MagickImageInspector().GetInfoAsync(result.FinalPage);
        Assert.Equal(new ImageSize(200, 200), finalInfo.Size);
        Assert.Equal(300, finalInfo.Density!.Value.Horizontal, precision: 2);
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
