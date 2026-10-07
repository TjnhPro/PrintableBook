using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Books;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class PhysicalBookCloneServiceTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BookClone.{Guid.NewGuid():N}");
    private readonly PhysicalFileSystem fileSystem = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloneAsync_copies_both_layouts_and_preserves_only_reusable_configuration(bool nestedLayout)
    {
        var (paths, source, files) = await CreateSourceAsync("AnimalBook_de", nestedLayout);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var shuffleStore = new JsonInteriorShuffleStore(fileSystem);
        var state = BookProcessingState.NotStarted(source.Id) with
        {
            Status = BookProcessingStatus.Completed,
            LastCompletedStep = "publish",
            UpdatedAt = DateTimeOffset.UtcNow,
            PublishedArtifactReferences = [Path.Combine(source.Directory.Value, "Output", "old.pdf")],
            SelectedCoverReference = files.Cover,
            InteriorFrameOverrides = new Dictionary<string, FrameMode>(StringComparer.OrdinalIgnoreCase)
            {
                [files.PageOneKey] = FrameMode.Enabled
            },
            HasBackground = false,
            InactiveInteriorSourceKeys = [files.PageTwoKey],
            HasIntro = true,
            SelectedIntroInteriorSourceKeys = [files.IntroKey],
            Metadata = BookProductionMetadata.Create(
                "Animal Days",
                "Relaxing Edition",
                "Cute Animal Coloring Pages",
                "Description",
                "Jane Doe",
                "ASIN-SHARED"),
            AssignedBrand = "Animal Brand",
            KeywordBuilder = new BookKeywordBuilderState(
                ["animal coloring"], "keyword one", null, null, null, null, null, null,
                "ads keyword", "ADS-ASIN", "build", DateTimeOffset.UtcNow, 4)
        };
        await stateStore.SaveAsync(source.Workspace, state);
        await shuffleStore.SaveAsync(source.Workspace, new InteriorShuffleMap(
            [new(new FileReference(files.PageTwo), 1), new(new FileReference(files.PageOne), 2)],
            73));
        await File.WriteAllTextAsync(Path.Combine(source.Workspace.WorkingDirectory.Value, "logs", "processing.jsonl"), "old log");
        await File.WriteAllTextAsync(Path.Combine(source.Workspace.WorkingDirectory.Value, "production", "final_cover.png"), "old production");
        Directory.CreateDirectory(Path.Combine(source.Directory.Value, "Output"));
        await File.WriteAllTextAsync(Path.Combine(source.Directory.Value, "Output", "old.pdf"), "old output");

        var result = await Service(stateStore, shuffleStore).CloneAsync(paths, source, Language("fr"));

        var destination = result.DestinationBook;
        Assert.Equal("AnimalBook_fr", destination.Name);
        Assert.Equal("AnimalBook_fr", destination.Id.Value);
        Assert.True(File.Exists(Path.Combine(destination.Directory.Value, "content.bin")));
        Assert.False(Directory.Exists(Path.Combine(destination.Directory.Value, "Output")));
        Assert.False(File.Exists(Path.Combine(destination.Workspace.WorkingDirectory.Value, "logs", "processing.jsonl")));
        Assert.False(File.Exists(Path.Combine(destination.Workspace.WorkingDirectory.Value, "production", "final_cover.png")));
        Assert.False(File.Exists(Path.Combine(destination.Workspace.WorkingDirectory.Value, "state", "production.json")));

        var clonedState = await stateStore.LoadAsync(destination.Workspace);
        Assert.NotNull(clonedState);
        Assert.Equal(BookProcessingStatus.NotStarted, clonedState.Status);
        Assert.Equal(DateTimeOffset.MinValue, clonedState.UpdatedAt);
        Assert.Null(clonedState.AssignedBrand);
        Assert.Null(clonedState.KeywordBuilder);
        Assert.Empty(clonedState.PublishedArtifactReferences!);
        Assert.Equal("fr", clonedState.LanguageCode);
        Assert.Equal("ASIN-SHARED", clonedState.Metadata!.Asin);
        Assert.Equal("Jane Doe", clonedState.Metadata.Author);
        Assert.False(clonedState.HasBackground);
        Assert.True(clonedState.HasIntro);
        Assert.Equal([files.PageTwoKey], clonedState.InactiveInteriorSourceKeys);
        Assert.Equal([files.IntroKey], clonedState.SelectedIntroInteriorSourceKeys);
        Assert.Equal(FrameMode.Enabled, clonedState.InteriorFrameOverrides![files.PageOneKey]);
        Assert.Equal(
            Path.Combine(destination.Directory.Value, Path.GetRelativePath(source.Directory.Value, files.Cover)),
            clonedState.SelectedCoverReference);

        var clonedShuffle = await shuffleStore.LoadAsync(destination.Workspace);
        Assert.Equal(73, clonedShuffle!.Seed);
        Assert.Equal(
            [
                Path.Combine(destination.Directory.Value, Path.GetRelativePath(source.Directory.Value, files.PageTwo)),
                Path.Combine(destination.Directory.Value, Path.GetRelativePath(source.Directory.Value, files.PageOne))
            ],
            clonedShuffle.Entries.OrderBy(entry => entry.OutputIndex).Select(entry => entry.Page.Value));
        Assert.Empty(StagingDirectories());

        await File.WriteAllTextAsync(Path.Combine(destination.Directory.Value, "content.bin"), "changed");
        Assert.Equal("source content", await File.ReadAllTextAsync(Path.Combine(source.Directory.Value, "content.bin")));
    }

    [Fact]
    public async Task CloneAsync_uses_an_English_default_source_state_when_none_exists()
    {
        var (paths, source, _) = await CreateSourceAsync("AnimalBook", nestedLayout: false);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var shuffleStore = new JsonInteriorShuffleStore(fileSystem);

        var result = await Service(stateStore, shuffleStore).CloneAsync(paths, source, Language("de"));

        var state = await stateStore.LoadAsync(result.DestinationBook.Workspace);
        Assert.Equal("de", state!.LanguageCode);
        Assert.Equal(BookProcessingStatus.NotStarted, state.Status);
        Assert.Null(state.Metadata);
        Assert.Null(state.AssignedBrand);
        Assert.Empty(StagingDirectories());
    }

    [Theory]
    [InlineData("selected-cover")]
    [InlineData("shuffle")]
    public async Task CloneAsync_rejects_references_outside_the_source_without_publishing(string kind)
    {
        var (paths, source, files) = await CreateSourceAsync("AnimalBook", nestedLayout: false);
        var stateStore = new JsonBookWorkspaceStateStore(fileSystem);
        var shuffleStore = new JsonInteriorShuffleStore(fileSystem);
        var outside = Path.Combine(rootPath, "outside.png");
        await File.WriteAllTextAsync(outside, "outside");
        if (kind == "selected-cover")
        {
            await stateStore.SaveAsync(source.Workspace, BookProcessingState.NotStarted(source.Id) with
            {
                SelectedCoverReference = outside
            });
        }
        else
        {
            await shuffleStore.SaveAsync(source.Workspace, new InteriorShuffleMap(
                [new(new FileReference(outside), 1)],
                5));
        }

        var exception = await Assert.ThrowsAsync<BookCloneException>(() =>
            Service(stateStore, shuffleStore).CloneAsync(paths, source, Language("de")).AsTask());

        Assert.Equal("book_clone_state_invalid", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(paths.SourcesDirectory.Value, "AnimalBook_de")));
        Assert.Empty(StagingDirectories());
    }

    [Theory]
    [InlineData("state")]
    [InlineData("shuffle")]
    public async Task CloneAsync_rejects_corrupt_persisted_state_without_publishing(string kind)
    {
        var (paths, source, _) = await CreateSourceAsync("AnimalBook", nestedLayout: false);
        var stateFile = Path.Combine(source.Workspace.WorkingDirectory.Value, "state", kind == "state" ? "book-state.json" : "interior-shuffle.json");
        await File.WriteAllTextAsync(stateFile, "{not-json");

        var exception = await Assert.ThrowsAsync<BookCloneException>(() =>
            Service().CloneAsync(paths, source, Language("de")).AsTask());

        Assert.Equal("book_clone_state_invalid", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(paths.SourcesDirectory.Value, "AnimalBook_de")));
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_rejects_case_insensitive_collision_and_missing_source()
    {
        var (paths, source, _) = await CreateSourceAsync("AnimalBook", nestedLayout: false);
        Directory.CreateDirectory(Path.Combine(paths.SourcesDirectory.Value, "ANIMALBOOK_DE"));

        var collision = await Assert.ThrowsAsync<BookCloneException>(() =>
            Service().CloneAsync(paths, source, Language("de")).AsTask());
        Directory.Delete(source.Directory.Value, recursive: true);
        var missing = await Assert.ThrowsAsync<BookCloneException>(() =>
            Service().CloneAsync(paths, source, Language("fr")).AsTask());

        Assert.Equal("book_clone_destination_exists", collision.Code);
        Assert.Equal("book_clone_source_not_found", missing.Code);
        Assert.Empty(StagingDirectories());
    }

    [Fact]
    public async Task CloneAsync_cleans_staging_after_copy_failure_or_cancellation()
    {
        var (paths, source, _) = await CreateSourceAsync("AnimalBook", nestedLayout: false);
        var failing = new PhysicalBookCloneService(
            new PhysicalBookWorkspaceFactory(fileSystem),
            new JsonBookWorkspaceStateStore(fileSystem),
            new JsonInteriorShuffleStore(fileSystem),
            (_, _, _) => ValueTask.FromException(new IOException("simulated copy failure")));

        var failure = await Assert.ThrowsAsync<BookCloneException>(() =>
            failing.CloneAsync(paths, source, Language("de")).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service().CloneAsync(paths, source, Language("fr"), cancellation.Token).AsTask());

        Assert.Equal("book_clone_failed", failure.Code);
        Assert.False(Directory.Exists(Path.Combine(paths.SourcesDirectory.Value, "AnimalBook_de")));
        Assert.False(Directory.Exists(Path.Combine(paths.SourcesDirectory.Value, "AnimalBook_fr")));
        Assert.Empty(StagingDirectories());
    }

    private PhysicalBookCloneService Service(
        IBookWorkspaceStateStore? stateStore = null,
        IInteriorShuffleStore? shuffleStore = null) => new(
        new PhysicalBookWorkspaceFactory(fileSystem),
        stateStore ?? new JsonBookWorkspaceStateStore(fileSystem),
        shuffleStore ?? new JsonInteriorShuffleStore(fileSystem));

    private async Task<(ApplicationPaths Paths, DiscoveredBook Book, SourceFiles Files)> CreateSourceAsync(
        string name,
        bool nestedLayout)
    {
        var sources = Path.Combine(rootPath, "sources");
        var brands = Path.Combine(rootPath, "brands");
        Directory.CreateDirectory(sources);
        Directory.CreateDirectory(brands);
        var sourcePath = Path.Combine(sources, name);
        Directory.CreateDirectory(sourcePath);
        var processingRoot = nestedLayout ? Path.Combine(sourcePath, "Clone book") : sourcePath;
        var coverDirectory = Path.Combine(processingRoot, nestedLayout ? "Book cover" : "Cover");
        var interiorDirectory = Path.Combine(processingRoot, nestedLayout ? "Book interior" : "Interior");
        var introDirectory = Path.Combine(processingRoot, "Intro");
        Directory.CreateDirectory(coverDirectory);
        Directory.CreateDirectory(interiorDirectory);
        Directory.CreateDirectory(introDirectory);
        var cover = Path.Combine(coverDirectory, "cover.png");
        var pageOne = Path.Combine(interiorDirectory, "page-01.png");
        var pageTwo = Path.Combine(interiorDirectory, "page-02.png");
        var intro = Path.Combine(introDirectory, "intro.png");
        await File.WriteAllTextAsync(cover, "cover");
        await File.WriteAllTextAsync(pageOne, "page one");
        await File.WriteAllTextAsync(pageTwo, "page two");
        await File.WriteAllTextAsync(intro, "intro");
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "content.bin"), "source content");

        var id = new BookId(name);
        var directory = new DirectoryReference(sourcePath);
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(id, directory);
        var paths = new ApplicationPaths(
            new DirectoryReference(rootPath),
            new DirectoryReference(brands),
            new DirectoryReference(sources),
            new FileReference(Path.Combine(rootPath, "settings.json")));
        return (
            paths,
            new DiscoveredBook(name, id, directory, workspace),
            new SourceFiles(
                cover,
                pageOne,
                pageTwo,
                intro,
                InteriorSourceKey.FromBookRoot(directory, new FileReference(pageOne)),
                InteriorSourceKey.FromBookRoot(directory, new FileReference(pageTwo)),
                InteriorSourceKey.FromBookRoot(directory, new FileReference(intro))));
    }

    private static SupportedLanguageOption Language(string code)
    {
        Assert.True(SupportedLanguageCatalog.TryGet(code, out var language));
        return language;
    }

    private string[] StagingDirectories() => Directory.Exists(rootPath)
        ? Directory.GetDirectories(rootPath, ".book-clone-*", SearchOption.TopDirectoryOnly)
        : [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private sealed record SourceFiles(
        string Cover,
        string PageOne,
        string PageTwo,
        string Intro,
        string PageOneKey,
        string PageTwoKey,
        string IntroKey);
}
