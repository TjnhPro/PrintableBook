using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class OrderedBookAssemblerTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.AssemblyTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task AssembleAsync_preserves_intro_order_and_applies_saved_interior_output_indices()
    {
        Directory.CreateDirectory(rootPath);
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(
            new BookId("book-one"), new DirectoryReference(Path.Combine(rootPath, "Book")));
        var intro = await CreatePngAsync("intro.png");
        var sourceOne = new FileReference("source-1.png");
        var sourceTwo = new FileReference("source-2.png");
        var finalOne = await CreatePngAsync("final-1.png");
        var finalTwo = await CreatePngAsync("final-2.png");
        var map = new InteriorShuffleMap(
            [new InteriorShuffleEntry(sourceOne, 2), new InteriorShuffleEntry(sourceTwo, 1)], 7);
        var assembler = new OrderedBookAssembler(fileSystem, new MagickImageInspector());

        var assembly = await assembler.AssembleAsync(new OrderedBookAssemblyRequest(
            workspace,
            [intro],
            [
                new InteriorPageProcessingResult("one", sourceOne, finalOne),
                new InteriorPageProcessingResult("two", sourceTwo, finalTwo)
            ],
            map,
            new ImageSize(100, 100)));

        Assert.Equal([intro, finalTwo, finalOne], assembly.OrderedPages);
    }

    private async Task<FileReference> CreatePngAsync(string filename)
    {
        var path = Path.Combine(rootPath, filename);
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            image.Write(path);
        }

        await Task.CompletedTask;
        return new FileReference(path);
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
