using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Application.Results;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application.BackgroundTasks;

public sealed class ProcessingSessionWorkerTests
{
    [Fact]
    public async Task Execution_gets_a_fresh_snapshot_then_reports_an_immutable_terminal_view()
    {
        var provider = new Provider(Snapshot());
        var application = new Application();
        var frame = new FrameResolver();
        IBackgroundTaskWorker worker = CreateWorker(provider, application, frame, new FileSystem(), new ImageInspector());
        var context = new Context();

        await worker.ExecuteAsync(Request(), context, CancellationToken.None);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(0, frame.Calls);
        Assert.NotNull(application.Request);
        Assert.False(context.View!.IsActive);
        Assert.Equal("Completed", context.View.CurrentStep);
    }

    [Fact]
    public async Task Validation_failure_uses_a_safe_code_and_terminal_view_before_resolving_a_frame()
    {
        var context = new Context();
        IBackgroundTaskWorker worker = CreateWorker(new Provider(Snapshot() with { Discovery = Snapshot().Discovery with { Brands = [] } }), new Application(), new FrameResolver(), new FileSystem(), new ImageInspector());

        var failure = await Assert.ThrowsAsync<BackgroundTaskFailureException>(() => worker.ExecuteAsync(Request(), context, CancellationToken.None).AsTask());

        Assert.Equal("process_brand_not_found", failure.Code);
        Assert.False(context.View!.IsActive);
        Assert.Equal("Failed", context.View.CurrentStep);
    }

    [Theory]
    [InlineData(BrandValidationStatus.NotValidated)]
    [InlineData(BrandValidationStatus.NeedsValidation)]
    public async Task Processing_stops_before_output_work_when_brand_is_not_certified(BrandValidationStatus status)
    {
        var application = new Application();
        var validation = new Validation(status);
        IBackgroundTaskWorker worker = CreateWorker(new Provider(Snapshot()), application, new FrameResolver(), new FileSystem(), new ImageInspector(), validation);

        var failure = await Assert.ThrowsAsync<BackgroundTaskFailureException>(() => worker.ExecuteAsync(Request(), new Context(), CancellationToken.None).AsTask());

        Assert.Equal("process_brand_not_validated", failure.Code);
        Assert.Null(application.Request);
        Assert.Equal(1, validation.CheckCalls);
    }

    [Fact]
    public async Task Processing_gate_uses_the_fresh_snapshot_settings()
    {
        var settings = GlobalSettings.Default with { FinalPageWidth = 901 };
        var validation = new Validation();
        IBackgroundTaskWorker worker = CreateWorker(new Provider(Snapshot(settings: settings)), new Application(), new FrameResolver(), new FileSystem(), new ImageInspector(), validation);

        await worker.ExecuteAsync(Request(), new Context(), CancellationToken.None);

        Assert.Equal(settings, validation.Settings);
        Assert.Equal(new DirectoryReference("brand"), validation.Directory);
    }

    [Fact]
    public async Task Requested_cancellation_with_a_cancelled_book_publishes_the_terminal_view_then_throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new Context();
        IBackgroundTaskWorker worker = CreateWorker(
            new Provider(Snapshot()),
            new Application(new BookProcessingQueueResult(false, [new BookProcessingQueueBookResult(new BookId("book-one"), BookProcessingStatus.Cancelled, null, null)])),
            new FrameResolver(), new FileSystem(), new ImageInspector());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ExecuteAsync(Request(), context, cancellation.Token).AsTask());

        Assert.False(context.View!.IsActive);
        Assert.Equal("Cancelled", context.View.CurrentStep);
        Assert.Collection(context.View.Queue, entry => Assert.Equal(BookProcessingStatus.Cancelled, entry.Status));
    }

    private static ProcessingSessionWorkerRequest Request() => new(["book-one"], "Brand", BookProcessingMode.InteriorOnly, DateTimeOffset.UtcNow);

    private static ProcessingSessionWorker CreateWorker(
        IApplicationSnapshotProvider provider,
        IPrintableBookApplication application,
        IBrandFrameResolver ignoredFrameResolver,
        IFileSystem fileSystem,
        IImageInspector imageInspector,
        IBrandValidationService? validation = null) =>
        new(provider, application, fileSystem, imageInspector, validation ?? new Validation());

    [Fact]
    public async Task Background_disabled_does_not_require_or_inspect_a_file()
    {
        var files = new FileSystem(false);
        var inspector = new ImageInspector(new InvalidDataException("must not be called"));
        var application = new Application();
        IBackgroundTaskWorker worker = CreateWorker(new Provider(Snapshot()), application, new FrameResolver(), files, inspector);

        await worker.ExecuteAsync(Request(), new Context(), CancellationToken.None);

        Assert.NotNull(application.Request);
        Assert.Equal(0, files.Calls);
        Assert.Equal(0, inspector.Calls);
        Assert.Null(Assert.Single(application.Request!.Books).BackgroundPage);
    }

    [Fact]
    public async Task Background_enabled_uses_the_certified_brand_file_without_reinspection()
    {
        var application = new Application();
        IBackgroundTaskWorker worker = CreateWorker(new Provider(Snapshot(hasBackground: true)), application, new FrameResolver(), new FileSystem(false), new ImageInspector());

        await worker.ExecuteAsync(Request(), new Context(), CancellationToken.None);

        Assert.Equal(new FileReference(Path.Combine("brand", "background.png")), Assert.Single(application.Request!.Books).BackgroundPage);
    }

    [Fact]
    public async Task Background_enabled_does_not_reinspect_the_certified_brand_file()
    {
        var application = new Application();
        var inspector = new ImageInspector(new InvalidDataException("must not inspect Brand files"));
        IBackgroundTaskWorker worker = CreateWorker(new Provider(Snapshot(hasBackground: true)), application, new FrameResolver(), new FileSystem(false), inspector);

        await worker.ExecuteAsync(Request(), new Context(), CancellationToken.None);

        Assert.NotNull(application.Request);
        Assert.Equal(0, inspector.Calls);
    }

    [Fact]
    public async Task Custom_intro_validation_cancellation_propagates_as_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var application = new Application();
        IBackgroundTaskWorker worker = CreateWorker(
            new Provider(Snapshot() with { BookSummaries = [Snapshot().BookSummaries[0] with { HasIntro = true, SelectedIntroInteriorSourceKeys = ["Book interior/page-001.png"] }] }),
            application,
            new FrameResolver(),
            new FileSystem(true),
            new ImageInspector(new OperationCanceledException(cancellation.Token)));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ExecuteAsync(Request(), new Context(), cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Null(application.Request);
    }

    [Fact]
    public async Task Background_enabled_passes_a_valid_page_using_the_effective_final_size()
    {
        var settings = GlobalSettings.Default with { FinalPageWidth = 901, FinalPageHeight = 902 };
        var application = new Application();
        IBackgroundTaskWorker worker = CreateWorker(new Provider(Snapshot(hasBackground: true, settings)), application, new FrameResolver(), new FileSystem(true), new ImageInspector(new ImageSize(901, 902)));

        await worker.ExecuteAsync(Request(), new Context(), CancellationToken.None);

        Assert.Equal(new FileReference(Path.Combine("brand", "background.png")), Assert.Single(application.Request!.Books).BackgroundPage);
    }

    [Fact]
    public async Task Mixed_queue_passes_one_validated_background_only_to_books_that_enable_it()
    {
        var application = new Application();
        var files = new FileSystem(true);
        var inspector = new ImageInspector();
        IBackgroundTaskWorker worker = CreateWorker(new Provider(MixedSnapshot()), application, new FrameResolver(), files, inspector);

        await worker.ExecuteAsync(new ProcessingSessionWorkerRequest(["book-one", "book-two"], "Brand", BookProcessingMode.InteriorOnly, DateTimeOffset.UtcNow), new Context(), CancellationToken.None);

        Assert.Equal(0, files.Calls);
        Assert.Equal(0, inspector.Calls);
        Assert.Collection(application.Request!.Books,
            first => Assert.Equal(new FileReference(Path.Combine("brand", "background.png")), first.BackgroundPage),
            second => Assert.Null(second.BackgroundPage));
    }

    [Fact]
    public async Task Resolves_automatic_intro_pages_in_filename_order()
    {
        var initial = Snapshot();
        var brand = Brand() with
        {
            IntroTemplateAssets =
            [
                new DiscoveredIntroTemplateAsset("intro-02.png", Path.Combine("brand", "IntroTemplate", "intro-02.png"), "intro-02.png", "file:///intro-02.png"),
                new DiscoveredIntroTemplateAsset("intro-01.png", Path.Combine("brand", "IntroTemplate", "intro-01.png"), "intro-01.png", "file:///intro-01.png")
            ]
        };
        var application = new Application();
        IBackgroundTaskWorker worker = CreateWorker(
            new Provider(initial with { Discovery = initial.Discovery with { Brands = [brand] } }),
            application,
            new FrameResolver(),
            new FileSystem(),
            new ImageInspector());

        await worker.ExecuteAsync(Request(), new Context(), CancellationToken.None);

        Assert.Equal(
            [
                new FileReference(Path.Combine("brand", "IntroTemplate", "intro-01.png")),
                new FileReference(Path.Combine("brand", "IntroTemplate", "intro-02.png"))
            ],
            Assert.Single(application.Request!.Books).EffectiveIntroTemplatePages);
    }

    [Fact]
    public async Task Resolves_custom_book_interior_intro_pages_in_the_saved_order()
    {
        var initial = Snapshot();
        var summary = initial.BookSummaries[0] with
        {
            HasIntro = true,
            SelectedIntroInteriorSourceKeys = ["Book interior/page-002.png", "Book interior/page-001.png"]
        };
        var application = new Application();
        IBackgroundTaskWorker worker = CreateWorker(
            new Provider(initial with { BookSummaries = [summary] }),
            application,
            new FrameResolver(),
            new FileSystem(),
            new ImageInspector());

        await worker.ExecuteAsync(Request(), new Context(), CancellationToken.None);

        Assert.Equal(
            [
                new FileReference(Path.Combine("book-one", "Book interior", "page-002.png")),
                new FileReference(Path.Combine("book-one", "Book interior", "page-001.png"))
            ],
            Assert.Single(application.Request!.Books).EffectiveIntroTemplatePages);
        Assert.True(Assert.Single(application.Request.Books).CustomIntroFromBookInterior);
    }

    [Theory]
    [InlineData(true, null, "process_intro_selection_required")]
    [InlineData(true, new[] { "Book interior/missing.png" }, "process_intro_selection_missing")]
    public async Task Rejects_invalid_custom_intro_selection(bool hasIntro, string[]? keys, string expectedCode)
    {
        var initial = Snapshot();
        var summary = initial.BookSummaries[0] with { HasIntro = hasIntro, SelectedIntroInteriorSourceKeys = keys };
        IBackgroundTaskWorker worker = CreateWorker(
            new Provider(initial with { BookSummaries = [summary] }),
            new Application(),
            new FrameResolver(),
            new FileSystem(),
            new ImageInspector());

        var failure = await Assert.ThrowsAsync<BackgroundTaskFailureException>(() => worker.ExecuteAsync(Request(), new Context(), CancellationToken.None).AsTask());

        Assert.Equal(expectedCode, failure.Code);
    }

    private static ApplicationSnapshot Snapshot(bool hasBackground = false, GlobalSettings? settings = null)
    {
        var bookId = new BookId("book-one");
        var workspace = new BookWorkspace(bookId, new DirectoryReference("workspace"), new DirectoryReference("processed"), new DirectoryReference("temp"));
        var book = new DiscoveredBook("Book One", bookId, new DirectoryReference("book-one"), workspace);
        return new ApplicationSnapshot(
            new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [Brand()], [book]),
            settings ?? GlobalSettings.Default,
            [new BookDesktopSummary(bookId, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 3, HasBackground: hasBackground, InteriorSourcePages:
            [
                new InteriorSourcePageSummary(Path.Combine("book-one", "Book interior", "page-001.png"), FrameMode.Auto, SourceKey: "Book interior/page-001.png"),
                new InteriorSourcePageSummary(Path.Combine("book-one", "Book interior", "page-002.png"), FrameMode.Auto, SourceKey: "Book interior/page-002.png"),
                new InteriorSourcePageSummary(Path.Combine("book-one", "Book interior", "page-003.png"), FrameMode.Auto, SourceKey: "Book interior/page-003.png")
            ])],
            DateTimeOffset.UtcNow);
    }

    private static ApplicationSnapshot MixedSnapshot()
    {
        var bookOneId = new BookId("book-one");
        var bookTwoId = new BookId("book-two");
        var bookOne = new DiscoveredBook("Book One", bookOneId, new DirectoryReference("book-one"), new BookWorkspace(bookOneId, new DirectoryReference("workspace-one"), new DirectoryReference("processed-one"), new DirectoryReference("temp-one")));
        var bookTwo = new DiscoveredBook("Book Two", bookTwoId, new DirectoryReference("book-two"), new BookWorkspace(bookTwoId, new DirectoryReference("workspace-two"), new DirectoryReference("processed-two"), new DirectoryReference("temp-two")));
        return new ApplicationSnapshot(
            new ApplicationDiscovery(new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")), [Brand()], [bookOne, bookTwo]),
            GlobalSettings.Default,
            [
                new BookDesktopSummary(bookOneId, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 1, HasBackground: true),
                new BookDesktopSummary(bookTwoId, "Ready", [], BookProcessingStatus.NotStarted, null, null, [], [], [], 1, HasBackground: false)
            ],
            DateTimeOffset.UtcNow);
    }

    private static DiscoveredBrand Brand() => new("Brand", new DirectoryReference("brand"), IntroTemplateAssets: [new DiscoveredIntroTemplateAsset("intro.png", Path.Combine("brand", "IntroTemplate", "intro.png"), "intro.png", "file:///intro.png")]);

    private sealed class Context : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("process-test");
        public ProcessSessionSnapshot? View { get; private set; }
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) { }
        public void SetView<TView>(TView view) where TView : class => View = view as ProcessSessionSnapshot;
    }

    private sealed class Provider(ApplicationSnapshot snapshot) : IApplicationSnapshotProvider
    {
        public int Calls { get; private set; }
        public ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default) { Calls++; return ValueTask.FromResult(snapshot); }
    }

    private sealed class FrameResolver : IBrandFrameResolver
    {
        public int Calls { get; private set; }
        public ValueTask<FileReference?> ResolveCompatibleFrameAsync(DiscoveredBrand brand, ImageSize targetSize, CancellationToken cancellationToken = default) { Calls++; return ValueTask.FromResult<FileReference?>(null); }
    }

    private sealed class FileSystem(bool exists = false) : IFileSystem
    {
        public int Calls { get; private set; }
        public ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default) { Calls++; return ValueTask.FromResult(exists || file.Value.Contains("IntroTemplate", StringComparison.OrdinalIgnoreCase) || file.Value.Contains("Book interior", StringComparison.OrdinalIgnoreCase)); }
        public ValueTask<FileMetadata?> GetFileMetadataAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult<FileMetadata?>(null);
        public ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask CreateDirectoryAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public async IAsyncEnumerable<FileReference> EnumerateFilesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public ValueTask<string> ReadTextAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult("");
        public ValueTask WriteTextAtomicallyAsync(FileReference file, string content, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CopyFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask MoveFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DeleteFileAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DeleteDirectoryAsync(DirectoryReference directory, bool recursive, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ImageInspector(ImageSize? size = null, Exception? exception = null) : IImageInspector
    {
        public ImageInspector(Exception exception) : this(null, exception) { }
        public int Calls { get; private set; }
        public ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (exception is not null) throw exception;
            if (image.Value.Contains("IntroTemplate", StringComparison.OrdinalIgnoreCase) || image.Value.Contains("Book interior", StringComparison.OrdinalIgnoreCase)) return ValueTask.FromResult(new ImageSize(1024, 1024));
            return ValueTask.FromResult(size ?? new ImageSize(GlobalSettings.Default.FinalPageWidth, GlobalSettings.Default.FinalPageHeight));
        }
        public ValueTask<ImageInfo> GetInfoAsync(FileReference image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Validation(BrandValidationStatus status = BrandValidationStatus.Validated) : IBrandValidationService
    {
        public int CheckCalls { get; private set; }
        public DirectoryReference? Directory { get; private set; }
        public GlobalSettings? Settings { get; private set; }

        public ValueTask<BrandValidationState> CheckStateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            Directory = brandDirectory;
            Settings = settings;
            return ValueTask.FromResult(new BrandValidationState(status));
        }

        public ValueTask<BrandValidationResult> ValidateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Application(BookProcessingQueueResult? result = null) : IPrintableBookApplication
    {
        public BookProcessingQueueRequest? Request { get; private set; }
        public ValueTask<ProcessingResult> ProcessAsync(PrintableBook.Core.Application.Commands.ProcessingRequest request, IProgress<PrintableBook.Core.Application.Progress.ProcessingProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BookProcessingQueueResult> ProcessBooksAsync(BookProcessingQueueRequest request, Action<BookProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Request = request;
            progress?.Invoke(new BookProcessingProgress(request.Books[0].BookId, BookProcessingStatus.Running, "interior-pages", 1, 1));
            return ValueTask.FromResult(result ?? new BookProcessingQueueResult(false, [BookProcessingQueueBookResult.CompletedInterior(request.Books[0].BookId, new PublishedInteriorOutput(new DirectoryReference("output"), new FileReference("interior.pdf")))]));
        }
    }
}
