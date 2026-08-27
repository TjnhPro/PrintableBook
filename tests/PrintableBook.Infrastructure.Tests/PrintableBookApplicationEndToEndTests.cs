using ImageMagick;
using PdfSharp.Pdf.IO;
using System.Security.Cryptography;
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
        var pagePipeline = CreatePagePipeline();
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
            new ImageSize(600, 300),
            new ImageSize(300, 300),
            new ImageSize(300, 300),
            new ImageSize(300, 300),
            new ImageDensity(300, 300),
            new PhysicalPageSize(2, 1),
            new PhysicalPageSize(1, 1),
            2,
            new ArtworkDetectionThreshold(20),
            null,
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
            Assert.Equal(144, coverPdf.Pages[0].Width.Point, precision: 3);
            Assert.Equal(72, coverPdf.Pages[0].Height.Point, precision: 3);
            Assert.Equal(72, interiorPdf.Pages[1].Height.Point, precision: 3);
        }
        var workspace = await workspaceFactory.CreateAsync(new BookId("sample-book"), bookDirectory);
        var state = await stateStore.LoadAsync(workspace);
        Assert.Equal(BookProcessingStatus.Completed, state!.Status);
        Assert.NotNull(state.ConfigurationFingerprint);
        Assert.Equal([bookResult.PublishedOutputs.CoverPdf.Value, bookResult.PublishedOutputs.InteriorPdf.Value], state.PublishedArtifactReferences);
        var publishedPreviews = state.PublishedInteriorPreviews!;
        Assert.Equal(["page-0001", "page-0002"], publishedPreviews.Select(preview => preview.PageId));
        Assert.All(publishedPreviews, preview =>
            Assert.Equal(Path.Combine(workspace.WorkingDirectory.Value, "processed", "interior", $"{preview.PageId}.png"), preview.FinalPagePath));
        var processingLog = await stateStore.LoadLogsAsync(workspace);
        Assert.Contains(processingLog, entry => entry.Event == "step.completed" && entry.Step == "publish");
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "state", "interior-shuffle.json")));
        var processedPage = Path.Combine(workspace.WorkingDirectory.Value, "processed", "interior", "page-0001.png");
        Assert.True(File.Exists(processedPage));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001", "prepared.png")));
        var finalPageInfo = await new MagickImageInspector().GetInfoAsync(new FileReference(
            processedPage));
        Assert.Equal(new ImageSize(300, 300), finalPageInfo.Size);
        Assert.Equal(300, finalPageInfo.Density!.Value.Horizontal, precision: 2);
        var processedPageHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(processedPage)));
        var processedPageTimestamp = File.GetLastWriteTimeUtc(processedPage);

        var reshuffled = await application.ProcessBooksAsync(new BookProcessingQueueRequest([command with { ShuffleSeed = 456 }]));
        var reshuffledBook = Assert.Single(reshuffled.Books);
        Assert.Equal(BookProcessingStatus.Completed, reshuffledBook.Status);
        Assert.Equal(456, (await new JsonInteriorShuffleStore(fileSystem).LoadAsync(workspace))!.Seed);
        Assert.Equal(bookResult.PublishedOutputs.InteriorPdf.Value, reshuffledBook.PublishedOutputs!.InteriorPdf.Value);
        Assert.Equal(processedPageHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(processedPage))));
        Assert.Equal(processedPageTimestamp, File.GetLastWriteTimeUtc(processedPage));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001", "prepared.png")));

        var rebuiltPdf = await application.ProcessBooksAsync(new BookProcessingQueueRequest([
            command with { ShuffleSeed = 456, InteriorPdfPageSize = new PhysicalPageSize(1.25, 1.25) }
        ]));
        var rebuiltBook = Assert.Single(rebuiltPdf.Books);
        using (var rebuiltInterior = PdfReader.Open(rebuiltBook.PublishedOutputs!.InteriorPdf.Value))
        {
            Assert.Equal(90, rebuiltInterior.Pages[0].Width.Point, precision: 3);
            Assert.Equal(90, rebuiltInterior.Pages[0].Height.Point, precision: 3);
        }
        Assert.Equal(processedPageHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(processedPage))));
        Assert.Equal(processedPageTimestamp, File.GetLastWriteTimeUtc(processedPage));

        var interruptedState = (await stateStore.LoadAsync(workspace))!
            .Start(DateTimeOffset.UtcNow)
            .BeginStep("interior-pages", DateTimeOffset.UtcNow);
        await stateStore.SaveAsync(workspace, interruptedState);
        var recovered = await application.ProcessBooksAsync(new BookProcessingQueueRequest([
            command with { ShuffleSeed = 456, InteriorPdfPageSize = new PhysicalPageSize(1.25, 1.25) }
        ]));
        Assert.Equal(BookProcessingStatus.Completed, Assert.Single(recovered.Books).Status);
        Assert.Equal(BookProcessingStatus.Completed, (await stateStore.LoadAsync(workspace))!.Status);
        Assert.Equal(processedPageHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(processedPage))));
        Assert.Equal(processedPageTimestamp, File.GetLastWriteTimeUtc(processedPage));
    }

    [Fact]
    public async Task ProcessBooksAsync_processes_book_interior_without_a_cover_and_publishes_only_the_interior_pdf()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "InteriorOnlyBook"));
        await CreateInteriorOnlyBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem),
            workspaceFactory,
            stateStore,
            new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem),
            CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var application = new PrintableBookApplication(
            new BookProcessingPipeline(Array.Empty<IBookProcessingStage>()),
            new BookProcessingQueueProcessor(new ProcessingSessionGate(), processor));
        var command = CreateCommand("interior-only-book", bookDirectory) with { Mode = BookProcessingMode.InteriorOnly };

        var result = await application.ProcessBooksAsync(new BookProcessingQueueRequest([command]));

        var bookResult = Assert.Single(result.Books);
        Assert.Equal(BookProcessingStatus.Completed, bookResult.Status);
        Assert.Null(bookResult.PublishedOutputs);
        Assert.NotNull(bookResult.PublishedInteriorOutput);
        Assert.True(File.Exists(bookResult.PublishedInteriorOutput!.InteriorPdf.Value));
        using var interiorPdf = PdfReader.Open(bookResult.PublishedInteriorOutput.InteriorPdf.Value);
        Assert.Equal(2, interiorPdf.Pages.Count);
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        var state = await stateStore.LoadAsync(workspace);
        Assert.Equal([bookResult.PublishedInteriorOutput.InteriorPdf.Value], state!.PublishedArtifactReferences);
        Assert.Contains(await stateStore.LoadLogsAsync(workspace), entry => entry.Event == "cover-validation.skipped");
    }

    [Fact]
    public async Task ProcessBookAsync_rejects_an_unsupported_only_interior_after_discovery()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "UnsupportedInteriorBook"));
        var interiorDirectory = Path.Combine(bookDirectory.Value, "Book interior");
        Directory.CreateDirectory(interiorDirectory);
        await File.WriteAllTextAsync(Path.Combine(interiorDirectory, "notes.txt"), "not an image");

        var fileSystem = new PhysicalFileSystem();
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem),
            new PhysicalBookWorkspaceFactory(fileSystem),
            new JsonBookWorkspaceStateStore(fileSystem),
            new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem),
            CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = CreateCommand("unsupported-interior-book", bookDirectory) with { Mode = BookProcessingMode.InteriorOnly };

        var result = await processor.ProcessBookAsync(command);

        Assert.Equal(BookProcessingStatus.Failed, result.Status);
        Assert.Equal("book.interior_empty", result.Failure!.Code);
    }

    [Fact]
    public async Task ProcessBooksAsync_rejects_a_coverless_book_in_full_book_mode()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "CoverlessFullBook"));
        await CreateInteriorOnlyBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem),
            workspaceFactory,
            stateStore,
            new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem),
            CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var application = new PrintableBookApplication(
            new BookProcessingPipeline(Array.Empty<IBookProcessingStage>()),
            new BookProcessingQueueProcessor(new ProcessingSessionGate(), processor));
        var command = CreateCommand("coverless-full-book", bookDirectory);

        var result = await application.ProcessBooksAsync(new BookProcessingQueueRequest([command]));

        var bookResult = Assert.Single(result.Books);
        Assert.Equal(BookProcessingStatus.Failed, bookResult.Status);
        Assert.Equal("book.cover_selection_required", bookResult.Failure!.Code);
        Assert.Null(bookResult.PublishedOutputs);
        Assert.Null(bookResult.PublishedInteriorOutput);
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        var state = await stateStore.LoadAsync(workspace);
        Assert.Equal(BookProcessingStatus.Failed, state!.Status);
        Assert.Contains(BookProcessingMode.FullBook.ToString(), state.ConfigurationFingerprint);
    }

    [Fact]
    public async Task ProcessBookAsync_skips_inactive_interior_without_renumbering_or_deleting_its_cache()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "InactiveInteriorBook"));
        await CreateInteriorOnlyBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem), workspaceFactory, stateStore, new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem), CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()), new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = CreateCommand("inactive-interior-book", bookDirectory) with { Mode = BookProcessingMode.InteriorOnly };
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);

        Assert.Equal(BookProcessingStatus.Completed, (await processor.ProcessBookAsync(command)).Status);
        var secondCache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0002", "prepared.png");
        Assert.True(File.Exists(secondCache));
        var secondProcessed = Path.Combine(workspace.WorkingDirectory.Value, "processed", "interior", "page-0002.png");
        var secondProcessedTimestamp = File.GetLastWriteTimeUtc(secondProcessed);

        var secondSource = new FileReference(Path.Combine(bookDirectory.Value, "Book interior", "page-02.png"));
        var state = (await stateStore.LoadAsync(workspace))!;
        await stateStore.SaveAsync(workspace, state.SetInteriorActive(InteriorSourceKey.FromBookRoot(bookDirectory, secondSource), false));

        var inactiveRun = await processor.ProcessBookAsync(command);
        Assert.Equal(BookProcessingStatus.Completed, inactiveRun.Status);
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "processed", "interior", "page-0001.png")));
        Assert.Equal(secondProcessedTimestamp, File.GetLastWriteTimeUtc(secondProcessed));
        Assert.True(File.Exists(secondCache));
        var inactiveMap = (await new JsonInteriorShuffleStore(fileSystem).LoadAsync(workspace))!;
        Assert.Equal([Path.Combine(bookDirectory.Value, "Book interior", "page-01.png")], inactiveMap.Entries.Select(entry => entry.Page.Value));

        state = (await stateStore.LoadAsync(workspace))!;
        await stateStore.SaveAsync(workspace, state.SetInteriorActive(InteriorSourceKey.FromBookRoot(bookDirectory, secondSource), true));
        Assert.Equal(BookProcessingStatus.Completed, (await processor.ProcessBookAsync(command)).Status);
        Assert.True(File.Exists(secondProcessed));
    }

    [Fact]
    public async Task ProcessBookAsync_rejects_a_book_when_all_interior_pages_are_inactive()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "NoActiveInteriorBook"));
        await CreateInteriorOnlyBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem), workspaceFactory, stateStore, new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem), CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()), new MagickPrintableBookPdfExporter(),
            new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = CreateCommand("no-active-interior-book", bookDirectory) with { Mode = BookProcessingMode.InteriorOnly };
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        var inactive = BookProcessingState.NotStarted(command.BookId)
            .SetInteriorActive(InteriorSourceKey.FromBookRoot(bookDirectory, new FileReference(Path.Combine(bookDirectory.Value, "Book interior", "page-01.png"))), false)
            .SetInteriorActive(InteriorSourceKey.FromBookRoot(bookDirectory, new FileReference(Path.Combine(bookDirectory.Value, "Book interior", "page-02.png"))), false);
        await stateStore.SaveAsync(workspace, inactive);

        var result = await processor.ProcessBookAsync(command);

        Assert.Equal(BookProcessingStatus.Failed, result.Status);
        Assert.Equal("book.no_active_interior_pages", result.Failure!.Code);
    }

    [Fact]
    public async Task ProcessBookAsync_keeps_a_concrete_shuffle_seed_when_active_pages_change_or_a_legacy_map_is_upgraded()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "StableShuffleSeedBook"));
        await CreateInteriorOnlyBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var shuffleStore = new JsonInteriorShuffleStore(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem), workspaceFactory, stateStore, new MagickCoverValidator(), shuffleStore,
            CreatePagePipeline(), new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(), new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = CreateCommand("stable-shuffle-seed-book", bookDirectory) with { Mode = BookProcessingMode.InteriorOnly, ShuffleSeed = null };
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);

        Assert.Equal(BookProcessingStatus.Completed, (await processor.ProcessBookAsync(command)).Status);
        var initial = (await shuffleStore.LoadAsync(workspace))!;
        Assert.NotNull(initial.Seed);

        await shuffleStore.SaveAsync(workspace, initial with { Seed = null });
        Assert.Equal(BookProcessingStatus.Completed, (await processor.ProcessBookAsync(command)).Status);
        var legacyUpgraded = (await shuffleStore.LoadAsync(workspace))!;
        Assert.NotNull(legacyUpgraded.Seed);
        Assert.Equal(initial.Entries, legacyUpgraded.Entries);

        var secondSource = new FileReference(Path.Combine(bookDirectory.Value, "Book interior", "page-02.png"));
        var state = (await stateStore.LoadAsync(workspace))!;
        await stateStore.SaveAsync(workspace, state.SetInteriorActive(InteriorSourceKey.FromBookRoot(bookDirectory, secondSource), false));
        Assert.Equal(BookProcessingStatus.Completed, (await processor.ProcessBookAsync(command)).Status);
        var reduced = (await shuffleStore.LoadAsync(workspace))!;
        Assert.Equal(legacyUpgraded.Seed, reduced.Seed);
        Assert.Single(reduced.Entries);

        state = (await stateStore.LoadAsync(workspace))!;
        await stateStore.SaveAsync(workspace, state.SetInteriorActive(InteriorSourceKey.FromBookRoot(bookDirectory, secondSource), true));
        Assert.Equal(BookProcessingStatus.Completed, (await processor.ProcessBookAsync(command)).Status);
        var restored = (await shuffleStore.LoadAsync(workspace))!;
        Assert.Equal(legacyUpgraded.Seed, restored.Seed);
        var sources = new[]
        {
            new FileReference(Path.Combine(bookDirectory.Value, "Book interior", "page-01.png")),
            secondSource
        };
        Assert.Equal(InteriorShuffleIndexGenerator.Generate(sources, restored.Seed).Entries, restored.Entries);
    }

    [Fact]
    public async Task ProcessBookAsync_interleaves_a_real_background_without_invalidating_artwork_cache()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "BackgroundBook"));
        await CreateBookFixtureAsync(bookDirectory);
        var background = new FileReference(Path.Combine(rootPath, "brand-background.png"));
        await WriteImageAsync(background.Value, 1, 1, 298, 298);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var shuffleStore = new JsonInteriorShuffleStore(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem), workspaceFactory, stateStore, new MagickCoverValidator(), shuffleStore,
            CreatePagePipeline(), new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(), new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = CreateCommand("background-book", bookDirectory) with { ShuffleSeed = 73, BackgroundPage = background };

        var first = await processor.ProcessBookAsync(command);

        Assert.Equal(BookProcessingStatus.Completed, first.Status);
        using (var coverPdf = PdfReader.Open(first.PublishedOutputs!.CoverPdf.Value))
        using (var interiorPdf = PdfReader.Open(first.PublishedOutputs.InteriorPdf.Value))
        {
            Assert.Single(coverPdf.Pages);
            Assert.Equal(4, interiorPdf.Pages.Count);
        }
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        var firstArtworkCache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001", "prepared.png");
        var firstArtworkCacheTimestamp = File.GetLastWriteTimeUtc(firstArtworkCache);
        var shuffle = (await shuffleStore.LoadAsync(workspace))!;
        Assert.DoesNotContain(shuffle.Entries, entry => entry.Page == background);

        await WriteImageAsync(background.Value, 2, 2, 297, 297);
        var second = await processor.ProcessBookAsync(command);

        Assert.Equal(BookProcessingStatus.Completed, second.Status);
        Assert.Equal(firstArtworkCacheTimestamp, File.GetLastWriteTimeUtc(firstArtworkCache));
        using var replacementPdf = PdfReader.Open(second.PublishedOutputs!.InteriorPdf.Value);
        Assert.Equal(4, replacementPdf.Pages.Count);
    }

    [Fact]
    public async Task ProcessBookAsync_processes_ordered_intro_pages_before_interiors_and_interleaves_background()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "IntroBook"));
        await CreateBookFixtureAsync(bookDirectory);
        var introOne = new FileReference(Path.Combine(rootPath, "intro-01.png"));
        var introTwo = new FileReference(Path.Combine(rootPath, "intro-02.png"));
        var background = new FileReference(Path.Combine(rootPath, "intro-background.png"));
        await WriteImageAsync(introOne.Value, 40, 20, 900, 980, 1024, 1024);
        await WriteImageAsync(introTwo.Value, 20, 40, 980, 900, 1024, 1024);
        await WriteImageAsync(background.Value, 1, 1, 298, 298);

        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var shuffleStore = new JsonInteriorShuffleStore(fileSystem);
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem), workspaceFactory, stateStore, new MagickCoverValidator(), shuffleStore,
            CreatePagePipeline(), new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(), new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()));
        var command = CreateCommand("intro-book", bookDirectory) with
        {
            Mode = BookProcessingMode.InteriorOnly,
            ShuffleSeed = 73,
            BackgroundPage = background,
            IntroTemplatePages = [introTwo, introOne]
        };

        var result = await processor.ProcessBookAsync(command);

        Assert.Equal(BookProcessingStatus.Completed, result.Status);
        using (var interiorPdf = PdfReader.Open(result.PublishedInteriorOutput!.InteriorPdf.Value))
        {
            Assert.Equal(8, interiorPdf.Pages.Count);
        }
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        Assert.True(File.Exists(Path.Combine(workspace.ProcessedDirectory.Value, "intro", "intro-0001.png")));
        Assert.True(File.Exists(Path.Combine(workspace.ProcessedDirectory.Value, "intro", "intro-0002.png")));
        var log = await stateStore.LoadLogsAsync(workspace);
        var entries = log.ToArray();
        Assert.True(Array.FindIndex(entries, entry => entry.Event == "step.started" && entry.Step == "intro-pages") < Array.FindIndex(entries, entry => entry.Event == "step.started" && entry.Step == "interior-pages"));
        var shuffle = (await shuffleStore.LoadAsync(workspace))!;
        Assert.DoesNotContain(shuffle.Entries, entry => entry.Page == introOne || entry.Page == introTwo);

        var fullBook = await processor.ProcessBookAsync(command with { Mode = BookProcessingMode.FullBook });
        Assert.Equal(BookProcessingStatus.Completed, fullBook.Status);
        using (var fullInteriorPdf = PdfReader.Open(fullBook.PublishedOutputs!.InteriorPdf.Value))
        {
            Assert.Equal(8, fullInteriorPdf.Pages.Count);
        }

        var shuffleBeforeIntroOrderChange = (await shuffleStore.LoadAsync(workspace))!;
        Assert.Equal(BookProcessingStatus.Completed, (await processor.ProcessBookAsync(command with { IntroTemplatePages = [introOne, introTwo] })).Status);
        Assert.Equal(shuffleBeforeIntroOrderChange.Entries, (await shuffleStore.LoadAsync(workspace))!.Entries);
    }

    [Fact]
    public async Task ProcessBookAsync_persists_the_active_interior_step_while_the_page_pipeline_is_running()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "InterruptedBook"));
        await CreateBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var blockingPipeline = new BlockingInteriorPagePipeline(CreatePagePipeline());
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

    [Fact]
    public async Task ProcessBookAsync_commits_completed_state_when_cancellation_arrives_after_publish()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "CommittedBook"));
        await CreateBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        using var cancellation = new CancellationTokenSource();
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem),
            workspaceFactory,
            stateStore,
            new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem),
            CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(),
            new CancellingAfterPublishOutputPublisher(
                new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()),
                cancellation));
        var command = CreateCommand("committed-book", bookDirectory);

        var result = await processor.ProcessBookAsync(command, cancellationToken: cancellation.Token);

        Assert.Equal(BookProcessingStatus.Completed, result.Status);
        Assert.NotNull(result.PublishedOutputs);
        Assert.True(File.Exists(result.PublishedOutputs!.CoverPdf.Value));
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        var persistedState = await stateStore.LoadAsync(workspace);
        Assert.Equal(BookProcessingStatus.Completed, persistedState!.Status);
        Assert.Equal([result.PublishedOutputs.CoverPdf.Value, result.PublishedOutputs.InteriorPdf.Value], persistedState.PublishedArtifactReferences);
    }

    [Fact]
    public async Task ProcessBookAsync_records_cancellation_when_it_arrives_before_publish()
    {
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "CancelledBook"));
        await CreateBookFixtureAsync(bookDirectory);
        var fileSystem = new PhysicalFileSystem();
        var workspaceFactory = new PhysicalBookWorkspaceFactory(fileSystem);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        using var cancellation = new CancellationTokenSource();
        var blockingPublisher = new BlockingBeforePublishOutputPublisher();
        var processor = new WorkspaceBookProcessingQueueBookProcessor(
            new BookSourceScanner(fileSystem),
            workspaceFactory,
            stateStore,
            new MagickCoverValidator(),
            new JsonInteriorShuffleStore(fileSystem),
            CreatePagePipeline(),
            new OrderedBookAssembler(fileSystem, new MagickImageInspector()),
            new MagickPrintableBookPdfExporter(),
            blockingPublisher);
        var command = CreateCommand("cancelled-book", bookDirectory);

        var processing = processor.ProcessBookAsync(command, cancellationToken: cancellation.Token).AsTask();
        await blockingPublisher.WaitUntilStartedAsync();
        cancellation.Cancel();

        var result = await processing;
        Assert.Equal(BookProcessingStatus.Cancelled, result.Status);
        Assert.Null(result.PublishedOutputs);
        Assert.False(Directory.Exists(command.FinalOutputRoot.Value));
        var workspace = await workspaceFactory.CreateAsync(command.BookId, bookDirectory);
        Assert.Equal(BookProcessingStatus.Cancelled, (await stateStore.LoadAsync(workspace))!.Status);
    }

    private static DiskBackedInteriorPagePipeline CreatePagePipeline() => new(
        new ArtworkClassifier(new MagickBorderLineDetector(), new MagickBorderPixelDetector()),
        new ArtworkPreparationService(
            new BorderArtPreparationProcessor(
                new MagickBorderBoundsCropProcessor(),
                new MagickSquareCropProcessor(),
                new MagickArtworkResizeProcessor()),
            new FullArtPreparationProcessor(
                new MagickArtworkTrimProcessor(),
                new MagickSquareCropProcessor(),
                new MagickArtworkResizeProcessor()),
            new CropArtPreparationProcessor(
                new MagickArtworkTrimProcessor(),
                new MagickSquarePadProcessor(),
                new MagickArtworkResizeProcessor()),
            new MagickImageInspector()),
        new MagickFrameProcessor(),
        new MagickWorkingPageProcessor(),
        new MagickFinalInteriorPageProcessor(),
        new MagickImageInspector());

    private PrintableBookProcessingCommand CreateCommand(string bookId, DirectoryReference bookDirectory) => new(
        new BookId(bookId),
        bookDirectory,
        new DirectoryReference(Path.Combine(rootPath, "Final")),
        new ImageSize(600, 300),
        new ImageSize(300, 300),
        new ImageSize(300, 300),
        new ImageSize(300, 300),
        new ImageDensity(300, 300),
        new PhysicalPageSize(2, 1),
        new PhysicalPageSize(1, 1),
        2,
        new ArtworkDetectionThreshold(20),
        null,
        123);

    private async Task CreateBookFixtureAsync(DirectoryReference bookDirectory)
    {
        var coverDirectory = Path.Combine(bookDirectory.Value, "Cover");
        var interiorDirectory = Path.Combine(bookDirectory.Value, "Interior");
        Directory.CreateDirectory(coverDirectory);
        Directory.CreateDirectory(interiorDirectory);
        await WriteImageAsync(Path.Combine(coverDirectory, "cover.png"), 10, 10, 589, 289, 600, 300);
        await WriteImageAsync(Path.Combine(interiorDirectory, "page-01.png"), 40, 20, 259, 279);
        await WriteImageAsync(Path.Combine(interiorDirectory, "page-02.png"), 20, 40, 279, 259);
    }

    private async Task CreateInteriorOnlyBookFixtureAsync(DirectoryReference bookDirectory)
    {
        var interiorDirectory = Path.Combine(bookDirectory.Value, "Book interior");
        Directory.CreateDirectory(interiorDirectory);
        await WriteImageAsync(Path.Combine(interiorDirectory, "page-01.png"), 40, 20, 259, 279);
        await WriteImageAsync(Path.Combine(interiorDirectory, "page-02.png"), 20, 40, 279, 259);
    }

    private static Task WriteImageAsync(string path, int minX, int minY, int maxX, int maxY, uint width = 300, uint height = 300)
    {
        using var image = new MagickImage(MagickColors.White, width, height);
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

    private sealed class CancellingAfterPublishOutputPublisher(
        IBookOutputPublisher inner,
        CancellationTokenSource cancellation) : IBookOutputPublisher
    {
        public async ValueTask<PublishedBookOutputs> PublishAsync(
            BookOutputPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            var published = await inner.PublishAsync(request, cancellationToken);
            cancellation.Cancel();
            return published;
        }

        public ValueTask<PublishedInteriorOutput> PublishInteriorAsync(
            InteriorOutputPublicationRequest request,
            CancellationToken cancellationToken = default) =>
            inner.PublishInteriorAsync(request, cancellationToken);
    }

    private sealed class BlockingBeforePublishOutputPublisher : IBookOutputPublisher
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PublishedBookOutputs> PublishAsync(
            BookOutputPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The publisher should be cancelled before publishing output.");
        }

        public async ValueTask<PublishedInteriorOutput> PublishInteriorAsync(
            InteriorOutputPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The publisher should be cancelled before publishing output.");
        }

        public Task WaitUntilStartedAsync() => started.Task;
    }
}
