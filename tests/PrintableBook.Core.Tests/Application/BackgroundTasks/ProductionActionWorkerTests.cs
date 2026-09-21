using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Application.BackgroundTasks;

public sealed class ProductionActionWorkerTests
{
    [Theory]
    [InlineData(ProductionActionKind.ProcessInteriorCover, ProductionAssetKind.InteriorCover)]
    [InlineData(ProductionActionKind.ProcessBookOwner, ProductionAssetKind.BookOwner)]
    public async Task Page_actions_use_the_canonical_book_workspace_and_current_settings(
        ProductionActionKind action,
        ProductionAssetKind expectedAsset)
    {
        var pages = new PageService();
        IBackgroundTaskWorker worker = new ProductionActionWorker(new Provider(Snapshot()), pages, new CoverService());
        var context = new Context();

        var result = Assert.IsType<ProductionActionResult>(await worker.ExecuteAsync(
            new ProductionActionRequest("book-one", action),
            context,
            CancellationToken.None));

        Assert.Equal(expectedAsset, pages.AssetKind);
        Assert.Equal(new DirectoryReference("workspace"), pages.Workspace?.WorkingDirectory);
        Assert.Equal(GlobalSettings.Default, pages.Settings);
        Assert.Equal("processed.png", result.OutputReference);
        Assert.Contains(context.Steps, step => step.StartsWith("Processing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cover_action_publishes_to_the_books_existing_output_folder()
    {
        var cover = new CoverService();
        IBackgroundTaskWorker worker = new ProductionActionWorker(new Provider(Snapshot()), new PageService(), cover);

        var result = Assert.IsType<ProductionActionResult>(await worker.ExecuteAsync(
            new ProductionActionRequest("book-one", ProductionActionKind.BuildCoverPdf),
            new Context(),
            CancellationToken.None));

        Assert.Equal(new DirectoryReference(Path.Combine("book-one", "Output")), cover.Output);
        Assert.Equal("cover.pdf", result.OutputReference);
    }

    [Fact]
    public async Task Missing_book_returns_a_safe_failure()
    {
        IBackgroundTaskWorker worker = new ProductionActionWorker(new Provider(Snapshot() with
        {
            Discovery = Snapshot().Discovery with { Books = [] }
        }), new PageService(), new CoverService());

        var failure = await Assert.ThrowsAsync<BackgroundTaskFailureException>(() => worker.ExecuteAsync(
            new ProductionActionRequest("missing", ProductionActionKind.BuildCoverPdf),
            new Context(),
            CancellationToken.None).AsTask());

        Assert.Equal("book_not_found", failure.Code);
    }

    private static ApplicationSnapshot Snapshot()
    {
        var id = new BookId("book-one");
        var workspace = new BookWorkspace(id, new DirectoryReference("workspace"), new DirectoryReference("processed"), new DirectoryReference("temp"));
        var book = new DiscoveredBook("Book One", id, new DirectoryReference("book-one"), workspace);
        return new ApplicationSnapshot(
            new ApplicationDiscovery(
                new ApplicationPaths(new DirectoryReference("root"), new DirectoryReference("brands"), new DirectoryReference("sources"), new FileReference("settings.json")),
                [],
                [book]),
            GlobalSettings.Default,
            [],
            DateTimeOffset.UnixEpoch);
    }

    private sealed class Provider(ApplicationSnapshot snapshot) : IApplicationSnapshotProvider
    {
        public ValueTask<ApplicationSnapshot> GetFreshAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

    private sealed class PageService : IProductionPageProcessingService
    {
        public BookWorkspace? Workspace { get; private set; }
        public ProductionAssetKind? AssetKind { get; private set; }
        public GlobalSettings? Settings { get; private set; }

        public ValueTask<ProductionPageProcessingResult> ProcessAsync(BookWorkspace workspace, ProductionAssetKind assetKind, GlobalSettings settings, CancellationToken cancellationToken = default)
        {
            Workspace = workspace;
            AssetKind = assetKind;
            Settings = settings;
            return ValueTask.FromResult(new ProductionPageProcessingResult(assetKind, new FileReference("source.png"), new FileReference("processed.png"), DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class CoverService : IProductionCoverPdfService
    {
        public DirectoryReference? Output { get; private set; }

        public ValueTask<ProductionCoverPdfResult> BuildAsync(BookWorkspace workspace, DirectoryReference finalOutputRoot, CancellationToken cancellationToken = default)
        {
            Output = finalOutputRoot;
            return ValueTask.FromResult(new ProductionCoverPdfResult(new FileReference("cover.pdf"), ProductionCoverPdfService.CoverPageSize, DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class Context : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = new("production-test");
        public List<string> Steps { get; } = [];
        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) => Steps.Add(step);
        public void SetView<TView>(TView view) where TView : class { }
    }
}
