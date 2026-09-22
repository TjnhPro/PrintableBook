using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Production;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class ProductionAssetImportServiceTests : IAsyncLifetime
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.ProductionImport.{Guid.NewGuid():N}");

    [Fact]
    public async Task ImportAsync_creates_the_canonical_asset_and_records_metadata_state()
    {
        var (service, workspace, stateStore, inspector) = await CreateScenarioAsync();
        var source = await WritePngLikeFileAsync("selected.png", 1);
        inspector.Size = new ImageSize(1400, 1500);

        var result = await service.ImportAsync(workspace, ProductionAssetKind.InteriorCover, source);
        var state = await stateStore.LoadAsync(workspace);

        Assert.Equal(ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.InteriorCover), result.Destination);
        Assert.True(File.Exists(result.Destination.Value));
        Assert.Equal(new ImageSize(1400, 1500), result.Size);
        Assert.Equal(result.Signature, state.GetAsset(ProductionAssetKind.InteriorCover)!.Signature);
        Assert.Equal(2, inspector.Reads);
    }

    [Fact]
    public async Task ImportAsync_rejects_a_wrong_cover_size_before_replacing_the_previous_asset()
    {
        var (service, workspace, _, inspector) = await CreateScenarioAsync();
        var destination = ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.FinalCover);
        await File.WriteAllBytesAsync(destination.Value, [.. PngSignature, 42]);
        var source = await WritePngLikeFileAsync("wrong-cover.png", 99);
        inspector.Size = new ImageSize(1024, 1024);

        var error = await Assert.ThrowsAsync<ProductionAssetImportException>(
            () => service.ImportAsync(workspace, ProductionAssetKind.FinalCover, source).AsTask());

        Assert.Equal("production_cover_size_invalid", error.Code);
        Assert.Equal([.. PngSignature, 42], await File.ReadAllBytesAsync(destination.Value));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination.Value)!, "*.pending"));
    }

    [Fact]
    public async Task ImportAsync_overwrites_only_after_the_copied_png_is_validated()
    {
        var (service, workspace, _, inspector) = await CreateScenarioAsync();
        inspector.Size = new ImageSize(5242, 2626);
        var first = await WritePngLikeFileAsync("first.png", 1);
        var second = await WritePngLikeFileAsync("second.png", 2);

        await service.ImportAsync(workspace, ProductionAssetKind.FinalCover, first);
        await service.ImportAsync(workspace, ProductionAssetKind.FinalCover, second);

        Assert.Equal([.. PngSignature, 2], await File.ReadAllBytesAsync(ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.FinalCover).Value));
    }

    [Fact]
    public async Task ImportAsync_rejects_a_non_png_signature_without_touching_the_workspace_asset()
    {
        var (service, workspace, _, _) = await CreateScenarioAsync();
        var sourcePath = Path.Combine(rootPath, "fake.png");
        await File.WriteAllTextAsync(sourcePath, "not a png");

        var error = await Assert.ThrowsAsync<ProductionAssetImportException>(
            () => service.ImportAsync(workspace, ProductionAssetKind.BookOwner, new FileReference(sourcePath)).AsTask());

        Assert.Equal("production_asset_not_png", error.Code);
        Assert.False(File.Exists(ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.BookOwner).Value));
    }

    private async Task<(ProductionAssetImportService Service, BookWorkspace Workspace, JsonProductionWorkspaceStateStore StateStore, StubImageInspector Inspector)> CreateScenarioAsync()
    {
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(
            new BookId("book-one"),
            new DirectoryReference(Path.Combine(rootPath, "Book One")));
        var stateStore = new JsonProductionWorkspaceStateStore(fileSystem);
        var inspector = new StubImageInspector();
        return (new ProductionAssetImportService(fileSystem, inspector, stateStore), workspace, stateStore, inspector);
    }

    private async Task<FileReference> WritePngLikeFileAsync(string fileName, byte marker)
    {
        var path = Path.Combine(rootPath, fileName);
        Directory.CreateDirectory(rootPath);
        await File.WriteAllBytesAsync(path, [.. PngSignature, marker]);
        return new FileReference(path);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private sealed class StubImageInspector : IImageInspector
    {
        public ImageSize Size { get; set; } = new(1000, 1000);
        public int Reads { get; private set; }

        public ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(Size);
        }

        public ValueTask<ImageInfo> GetInfoAsync(FileReference image, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ImageInfo(Size, null));
    }
}
