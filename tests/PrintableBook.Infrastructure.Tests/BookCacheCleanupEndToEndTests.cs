using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Storage;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Pdf;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Scanning;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class BookCacheCleanupEndToEndTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.CacheCleanupTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Completed_book_can_be_cleaned_and_processed_again_without_reclassification_when_stamp_is_compatible()
    {
        var fixture = await CreateProcessedBookAsync("compatible-book");
        var cache = Path.Combine(fixture.Workspace.WorkingDirectory.Value, "cache", "page-0001");
        var classification = Path.Combine(cache, "classification.json");
        var stamp = Path.Combine(cache, "input-stamp.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var processed = Path.Combine(fixture.Workspace.ProcessedDirectory.Value, "interior", "page-0001.png");
        var published = fixture.First.PublishedInteriorOutput!.InteriorPdf.Value;
        var classificationBytes = await File.ReadAllBytesAsync(classification);

        await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(fixture.Workspace);

        Assert.True(File.Exists(classification));
        Assert.True(File.Exists(stamp));
        Assert.False(File.Exists(prepared));
        Assert.False(File.Exists(processed));
        Assert.True(File.Exists(published));

        var repeated = await fixture.Processor.ProcessBookAsync(fixture.Command);

        Assert.Equal(BookProcessingStatus.Completed, repeated.Status);
        Assert.True(File.Exists(repeated.PublishedInteriorOutput!.InteriorPdf.Value));
        Assert.True(File.Exists(prepared));
        Assert.True(File.Exists(processed));
        Assert.Equal(classificationBytes, await File.ReadAllBytesAsync(classification));
    }

    [Fact]
    public async Task Cleaned_book_reclassifies_when_source_changes()
    {
        var fixture = await CreateProcessedBookAsync("changed-book");
        var cache = Path.Combine(fixture.Workspace.WorkingDirectory.Value, "cache", "page-0001");
        var classification = Path.Combine(cache, "classification.json");
        var stamp = Path.Combine(cache, "input-stamp.json");
        var originalClassificationTime = File.GetLastWriteTimeUtc(classification);
        var originalStamp = await File.ReadAllTextAsync(stamp);

        await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(fixture.Workspace);
        await Task.Delay(TimeSpan.FromMilliseconds(25));
        await WriteInteriorAsync(Path.Combine(fixture.BookDirectory.Value, "Book interior", "page-01.png"), 80, 30);

        var repeated = await fixture.Processor.ProcessBookAsync(fixture.Command);

        Assert.Equal(BookProcessingStatus.Completed, repeated.Status);
        Assert.NotEqual(originalStamp, await File.ReadAllTextAsync(stamp));
        Assert.True(File.GetLastWriteTimeUtc(classification) > originalClassificationTime);
    }

    [Fact]
    public async Task Legacy_completed_workspace_cleanup_preserves_legacy_output_and_migrates_stamp()
    {
        var bookId = new BookId("legacy-book");
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "LegacyBook"));
        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(bookId, bookDirectory);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var legacyOutput = Path.Combine(rootPath, "outputs", "run-1", "legacy-book-interior.pdf");
        var legacyStamp = Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-0001.input-stamp.json");
        var migratedStamp = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001", "input-stamp.json");
        var prepared = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001", "prepared.png");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyOutput)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyStamp)!);
        Directory.CreateDirectory(Path.GetDirectoryName(prepared)!);
        await File.WriteAllBytesAsync(legacyOutput, [1, 2, 3]);
        await File.WriteAllTextAsync(legacyStamp, "legacy-stamp");
        await File.WriteAllBytesAsync(prepared, [4, 5, 6]);
        await stateStore.SaveAsync(workspace, BookProcessingState.NotStarted(bookId)
            .RecordPublishedArtifacts([legacyOutput])
            .Complete(DateTimeOffset.UtcNow));

        var book = new DiscoveredBook(bookId.Value, bookId, bookDirectory, workspace);
        IBackgroundTaskWorker worker = new CacheCleanupWorker(
            new StaticDiscovery([book]), stateStore, fileSystem, new PhysicalBookStorageMaintenance());
        var result = Assert.IsType<CacheCleanupResult>(await worker.ExecuteAsync(
            new CacheCleanupRequest(), new NoOpContext(), CancellationToken.None));

        Assert.Equal((1, 1, 0, 0), (result.ScannedBooks, result.CleanedBooks, result.SkippedBooks, result.FailedBooks));
        Assert.True(File.Exists(legacyOutput));
        Assert.False(File.Exists(legacyStamp));
        Assert.True(File.Exists(migratedStamp));
        Assert.False(File.Exists(prepared));
    }

    private async Task<ProcessedFixture> CreateProcessedBookAsync(string bookId)
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, bookId));
        await WriteInteriorAsync(Path.Combine(bookDirectory.Value, "Book interior", "page-01.png"), 40, 20);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem), workspaceFactory, new JsonBookWorkspaceStateStore(fileSystem),
            new MagickCoverValidator(), new JsonInteriorShuffleStore(fileSystem), CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()), new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = new PrintableBookProcessingCommand(
            new BookId(bookId), bookDirectory, new DirectoryReference(Path.Combine(bookDirectory.Value, "Output")),
            new ImageSize(300, 300), new ImageSize(300, 300), new ImageSize(300, 300), new ImageSize(300, 300),
            new ImageDensity(300, 300), new PhysicalPageSize(1, 1), new PhysicalPageSize(1, 1), 1,
            new ArtworkDetectionThreshold(20), null, 123) { Mode = BookProcessingMode.InteriorOnly };
        var first = await processor.ProcessBookAsync(command);
        Assert.Equal(BookProcessingStatus.Completed, first.Status);
        Assert.Equal(
            Path.Combine(bookDirectory.Value, "Output", $"{bookId} - Interior.pdf"),
            first.PublishedInteriorOutput!.InteriorPdf.Value);
        Assert.True(File.Exists(first.PublishedInteriorOutput.InteriorPdf.Value));
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        return new ProcessedFixture(bookDirectory, workspace, command, processor, first);
    }

    private static DiskBackedInteriorPagePipeline CreatePagePipeline() => new(
        new ArtworkClassifier(new MagickBorderLineDetector(), new MagickBorderPixelDetector()),
        new ArtworkPreparationService(
            new BorderArtPreparationProcessor(new MagickBorderBoundsCropProcessor(), new MagickSquareCropProcessor(), new MagickArtworkResizeProcessor()),
            new FullArtPreparationProcessor(new MagickArtworkTrimProcessor(), new MagickSquareCropProcessor(), new MagickArtworkResizeProcessor()),
            new CropArtPreparationProcessor(new MagickArtworkTrimProcessor(), new MagickSquarePadProcessor(), new MagickArtworkResizeProcessor()),
            new MagickImageInspector()),
        new MagickFrameProcessor(), new MagickWorkingPageProcessor(), new MagickFinalInteriorPageProcessor(), new MagickImageInspector());

    private static Task WriteInteriorAsync(string path, int firstX, int firstY)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new MagickImage(MagickColors.White, 300, 300);
        image.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
        image.GetPixels().SetPixel(firstX, firstY, [0, 0, 0]);
        image.GetPixels().SetPixel(259, 279, [0, 0, 0]);
        image.Write(path);
        return Task.CompletedTask;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private sealed record ProcessedFixture(
        DirectoryReference BookDirectory,
        BookWorkspace Workspace,
        PrintableBookProcessingCommand Command,
        WorkspaceBookProcessingQueueBookProcessor Processor,
        BookProcessingQueueBookResult First);

    private sealed class StaticDiscovery(IReadOnlyList<DiscoveredBook> books) : IApplicationRootDiscovery
    {
        public ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDiscovery(
                new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [], books));
    }

    private sealed class NoOpContext : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("cache-cleanup");
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class { }
    }
}
