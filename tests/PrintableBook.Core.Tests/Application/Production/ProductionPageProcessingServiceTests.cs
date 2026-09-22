using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Application.Production;

public sealed class ProductionPageProcessingServiceTests
{
    [Fact]
    public async Task ProcessAsync_builds_a_forced_no_frame_request_and_records_the_successful_page()
    {
        var workspace = new BookWorkspace(
            new BookId("book"),
            new DirectoryReference("C:\\book\\.workspace"),
            new DirectoryReference("C:\\book\\.workspace\\processed"),
            new DirectoryReference("C:\\book\\.workspace\\output-temp"));
        var source = ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.BookOwner);
        var output = ProductionWorkspacePaths.ProcessedFile(workspace, ProductionAssetKind.BookOwner);
        var files = new MetadataFileSystem(
            (source.Value, new FileMetadata(100, DateTimeOffset.Parse("2026-09-21T12:00:00Z"))),
            (output.Value, new FileMetadata(200, DateTimeOffset.Parse("2026-09-21T12:01:00Z"))));
        var pipeline = new CapturingPipeline(output);
        var state = new StateStore();
        var service = new ProductionPageProcessingService(files, pipeline, state);

        var result = await service.ProcessAsync(workspace, ProductionAssetKind.BookOwner, GlobalSettings.Default);

        Assert.Equal(output, result.FinalPage);
        Assert.Equal("production-book-owner", pipeline.Request!.PageId);
        Assert.Equal(InteriorPageProcessingKind.ProductionInterior, pipeline.Request.ProcessingKind);
        Assert.Equal("interior-book-owner.png", pipeline.Request.OutputFileName);
        Assert.Equal(FrameMode.Disabled, pipeline.Request.FrameMode);
        Assert.Null(pipeline.Request.Frame);
        var recorded = Assert.Single(state.State.ProcessedPages!);
        Assert.Equal("interior_book_owner.png", recorded.Key);
        Assert.StartsWith("sha256:", recorded.Value.ProcessingSettingsSignature, StringComparison.Ordinal);
        Assert.Equal(100, recorded.Value.SourceSignature.LengthBytes);
        Assert.Equal(200, recorded.Value.OutputSignature.LengthBytes);
    }

    [Fact]
    public void Settings_signature_is_stable_for_effective_defaults_and_changes_with_geometry()
    {
        var implicitDefaults = ProductionPageProcessingService.CreateSettingsSignature(GlobalSettings.Default);
        var explicitDefaults = ProductionPageProcessingService.CreateSettingsSignature(GlobalSettings.Default with
        {
            ArtworkSourceNormalization = ArtworkSourceNormalizationSettings.Default,
            BorderLineDetection = BorderLineDetectionSettings.Default
        });
        var changed = ProductionPageProcessingService.CreateSettingsSignature(GlobalSettings.Default with { FinalPageWidth = GlobalSettings.Default.FinalPageWidth + 1 });

        Assert.Equal(implicitDefaults, explicitDefaults);
        Assert.NotEqual(implicitDefaults, changed);
    }

    private sealed class CapturingPipeline(FileReference output) : IInteriorPagePipeline
    {
        public InteriorPagePipelineRequest? Request { get; private set; }

        public ValueTask<InteriorPageProcessingResult> ProcessAsync(InteriorPagePipelineRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(new InteriorPageProcessingResult(request.PageId, request.Source, output));
        }
    }

    private sealed class StateStore : IProductionWorkspaceStateStore
    {
        public ProductionWorkspaceState State { get; private set; } = ProductionWorkspaceState.Empty;
        public ValueTask<ProductionWorkspaceState> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult(State);
        public ValueTask SaveAsync(BookWorkspace workspace, ProductionWorkspaceState state, CancellationToken cancellationToken = default)
        {
            State = state;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MetadataFileSystem(params (string Path, FileMetadata Metadata)[] values) : IFileSystem
    {
        private readonly Dictionary<string, FileMetadata> metadata = values.ToDictionary(value => value.Path, value => value.Metadata, StringComparer.OrdinalIgnoreCase);

        public ValueTask<FileMetadata?> GetFileMetadataAsync(FileReference file, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(metadata.TryGetValue(file.Value, out var value) ? (FileMetadata?)value : null);
        public ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult(metadata.ContainsKey(file.Value));
        public ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask CreateDirectoryAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<FileReference> EnumerateFilesAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<string> ReadTextAsync(FileReference file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask WriteTextAtomicallyAsync(FileReference file, string content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask CopyFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask MoveFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DeleteFileAsync(FileReference file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DeleteDirectoryAsync(DirectoryReference directory, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
