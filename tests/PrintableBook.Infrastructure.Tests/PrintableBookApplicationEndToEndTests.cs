using ImageMagick;
using PdfSharp.Pdf.IO;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Pipelines;
using PrintableBook.Core.Application.Execution;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Pdf;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Scanning;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PrintableBookApplicationEndToEndTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.EndToEndTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ProcessBooksAsync_processes_a_real_book_folder_and_publishes_validated_outputs()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "SampleBook"));
        await CreateBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var pagePipeline = new DiskBackedInteriorPagePipeline(
            new MagickArtworkTrimProcessor(),
            new MagickSquareCanvasProcessor(),
            new MagickArtworkResizeProcessor(),
            new MagickFrameProcessor(),
            new MagickFinalInteriorPageProcessor(),
            new MagickImageInspector());
        var queueBookProcessor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem),
            workspaceFactory,
            stateStore,
            new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem),
            pagePipeline,
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var application = new PrintableBookApplication(
            new BookProcessingPipeline(Array.Empty<IBookProcessingStage>()),
            new BookProcessingQueueProcessor(new ProcessingSessionGate(), queueBookProcessor));

        var command = new PrintableBookProcessingCommand(
            new BookId("sample-book"),
            bookDirectory,
            new DirectoryReference(Path.Combine(rootPath, "Final")),
            new ImageSize(300, 300),
            new ImageSize(300, 300),
            new ImageDensity(300, 300),
            new PhysicalPageSize(1, 1),
            new PhysicalPageSize(1, 1),
            2,
            new ArtworkDetectionThreshold(20),
            null,
            false,
            123);
        var result = await application.ProcessBooksAsync(new BookProcessingQueueRequest([command]));

        var bookResult = Assert.Single(result.Books);
        Assert.False(result.IsAlreadyRunning);
        Assert.Equal(BookProcessingStatus.Completed, bookResult.Status);
        Assert.NotNull(bookResult.PublishedOutputs);
        Assert.True(File.Exists(bookResult.PublishedOutputs!.CoverPdf.Value));
        Assert.True(File.Exists(bookResult.PublishedOutputs.InteriorPdf.Value));
        using (var coverPdf = PdfReader.Open(bookResult.PublishedOutputs.CoverPdf.Value))
        using (var interiorPdf = PdfReader.Open(bookResult.PublishedOutputs.InteriorPdf.Value))
        {
            Assert.Single(coverPdf.Pages);
            Assert.Equal(2, interiorPdf.Pages.Count);
            Assert.Equal(72, coverPdf.Pages[0].Width.Point, precision: 3);
            Assert.Equal(72, interiorPdf.Pages[1].Height.Point, precision: 3);
        }
        var workspace = await workspaceFactory.CreateAsync(new BookId("sample-book"), bookDirectory);
        var state = await stateStore.LoadAsync(workspace);
        Assert.Equal(BookProcessingStatus.Completed, state!.Status);
        Assert.NotNull(state.ConfigurationFingerprint);
        Assert.Equal([bookResult.PublishedOutputs.CoverPdf.Value, bookResult.PublishedOutputs.InteriorPdf.Value], state.PublishedArtifactReferences);
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "state", "interior-shuffle.json")));
        Assert.True(File.Exists(Path.Combine(bookResult.PublishedOutputs.PublishedDirectory.Value, "interior", "page-0001.png")));
        var finalPageInfo = await new MagickImageInspector().GetInfoAsync(new FileReference(
            Path.Combine(bookResult.PublishedOutputs.PublishedDirectory.Value, "interior", "page-0001.png")));
        Assert.Equal(new ImageSize(300, 300), finalPageInfo.Size);
        Assert.Equal(300, finalPageInfo.Density!.Value.Horizontal, precision: 2);

        var reshuffled = await application.ProcessBooksAsync(new BookProcessingQueueRequest([command with { ShuffleSeed = 456 }]));
        Assert.Equal(BookProcessingStatus.Completed, Assert.Single(reshuffled.Books).Status);
        Assert.Equal(456, (await new JsonInteriorShuffleStore(fileSystem).LoadAsync(workspace))!.Seed);
    }

    [Fact]
    public async Task ProcessBookAsync_persists_the_active_interior_step_while_the_page_pipeline_is_running()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "InterruptedBook"));
        await CreateBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var blockingPipeline = new BlockingInteriorPagePipeline(new DiskBackedInteriorPagePipeline(
            new MagickArtworkTrimProcessor(),
            new MagickSquareCanvasProcessor(),
            new MagickArtworkResizeProcessor(),
            new MagickFrameProcessor(),
            new MagickFinalInteriorPageProcessor(),
            new MagickImageInspector()));
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem),
            workspaceFactory,
            stateStore,
            new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem),
            blockingPipeline,
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = CreateCommand("interrupted-book", bookDirectory);

        var processing = processor.ProcessBookAsync(command).AsTask();
        await blockingPipeline.WaitUntilStartedAsync();

        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        var stateWhileRunning = await stateStore.LoadAsync(workspace);
        Assert.Equal(BookProcessingStatus.Running, stateWhileRunning!.Status);
        Assert.Equal("interior-pages", stateWhileRunning.CurrentStep);
        Assert.Equal("cover-validation", stateWhileRunning.LastCompletedStep);

        blockingPipeline.Release();
        Assert.Equal(BookProcessingStatus.Completed, (await processing).Status);
    }

    private PrintableBookProcessingCommand CreateCommand(string bookId, DirectoryReference bookDirectory) => new(
        new BookId(bookId),
        bookDirectory,
        new DirectoryReference(Path.Combine(rootPath, "Final")),
        new ImageSize(300, 300),
        new ImageSize(300, 300),
        new ImageDensity(300, 300),
        new PhysicalPageSize(1, 1),
        new PhysicalPageSize(1, 1),
        2,
        new ArtworkDetectionThreshold(20),
        null,
        false,
        123);

    private async Task CreateBookFixtureAsync(DirectoryReference bookDirectory)
    {
        var coverDirectory = Path.Combine(bookDirectory.Value, "Cover");
        var interiorDirectory = Path.Combine(bookDirectory.Value, "Interior");
        Directory.CreateDirectory(coverDirectory);
        Directory.CreateDirectory(interiorDirectory);
        await WriteImageAsync(Path.Combine(coverDirectory, "cover.png"), 10, 10, 289, 289);
        await WriteImageAsync(Path.Combine(interiorDirectory, "page-01.png"), 40, 20, 259, 279);
        await WriteImageAsync(Path.Combine(interiorDirectory, "page-02.png"), 20, 40, 279, 259);
    }

    private static Task WriteImageAsync(string path, int minX, int minY, int maxX, int maxY)
    {
        using var image = new MagickImage(MagickColors.White, 300, 300);
        image.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
        image.GetPixels().SetPixel(minX, minY, [0, 0, 0]);
        image.GetPixels().SetPixel(maxX, maxY, [0, 0, 0]);
        image.Write(path);
        return Task.CompletedTask;
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

    private sealed class BlockingInteriorPagePipeline(IInteriorPagePipeline inner) : IInteriorPagePipeline
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<InteriorPageProcessingResult> ProcessAsync(
            InteriorPagePipelineRequest request,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await inner.ProcessAsync(request, cancellationToken);
        }

        public Task WaitUntilStartedAsync() => started.Task;

        public void Release() => release.TrySetResult();
    }
}
