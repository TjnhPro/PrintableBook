using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application;

public sealed class InteriorShuffleServiceTests
{
    [Fact]
    public async Task RandomizeAsync_persists_only_active_non_intro_pages()
    {
        var fixture = CreateFixture();
        var introKey = InteriorSourceKey.FromBookRoot(fixture.Book.Directory, fixture.Pages[0]);
        var inactiveKey = InteriorSourceKey.FromBookRoot(fixture.Book.Directory, fixture.Pages[1]);
        fixture.StateStore.State = BookProcessingState.NotStarted(fixture.Book.Id)
            .SetHasIntro(true)
            .SetIntroInteriorSourceKeys([introKey])
            .SetInteriorActive(inactiveKey, false);

        var result = await fixture.Service.RandomizeAsync(fixture.Book);

        Assert.Equal(2, result.PageCount);
        Assert.Equal(fixture.Pages.Skip(2).OrderBy(page => page.Value), fixture.ShuffleStore.Map!.Entries.Select(entry => entry.Page).OrderBy(page => page.Value));
        Assert.Equal(InteriorShuffleCompatibility.Current, InteriorShufflePolicy.Compatibility(fixture.ShuffleStore.Map, fixture.Pages.Skip(2).ToArray()));
    }

    [Fact]
    public async Task RandomizeAsync_guarantees_a_different_order_when_more_than_one_page_is_current()
    {
        var fixture = CreateFixture();
        await fixture.Service.RandomizeAsync(fixture.Book);
        var first = fixture.ShuffleStore.Map!;

        await fixture.Service.RandomizeAsync(fixture.Book);
        var second = fixture.ShuffleStore.Map!;

        Assert.False(InteriorShufflePolicy.HasSameOrder(first, second));
        Assert.Equal(InteriorShuffleCompatibility.Current, InteriorShufflePolicy.Compatibility(second, fixture.Pages));
    }

    [Fact]
    public async Task RandomizeAsync_rejects_a_book_without_active_pages()
    {
        var fixture = CreateFixture();
        fixture.StateStore.State = fixture.Pages.Aggregate(
            BookProcessingState.NotStarted(fixture.Book.Id),
            (state, page) => state.SetInteriorActive(InteriorSourceKey.FromBookRoot(fixture.Book.Directory, page), false));

        var exception = await Assert.ThrowsAsync<InteriorShuffleException>(() => fixture.Service.RandomizeAsync(fixture.Book).AsTask());

        Assert.Equal("interior_shuffle_no_active_pages", exception.Code);
        Assert.Null(fixture.ShuffleStore.Map);
    }

    [Fact]
    public async Task RandomizeAsync_replaces_an_invalid_saved_shuffle_file()
    {
        var fixture = CreateFixture();
        fixture.ShuffleStore.ThrowOnNextLoad = true;

        var result = await fixture.Service.RandomizeAsync(fixture.Book);

        Assert.Equal(4, result.PageCount);
        Assert.Equal(InteriorShuffleCompatibility.Current, InteriorShufflePolicy.Compatibility(fixture.ShuffleStore.Map, fixture.Pages));
    }

    private static Fixture CreateFixture()
    {
        var bookId = new BookId("book-one");
        var directory = new DirectoryReference(Path.Combine("root", "book-one"));
        var workspace = new BookWorkspace(
            bookId,
            new DirectoryReference(Path.Combine("root", ".workspace", "book-one")),
            new DirectoryReference(Path.Combine("root", ".workspace", "book-one", "processed")),
            new DirectoryReference(Path.Combine("root", ".workspace", "book-one", "output")));
        var pages = Enumerable.Range(1, 4)
            .Select(index => new FileReference(Path.Combine(directory.Value, "Book interior", $"page-{index:00}.png")))
            .ToArray();
        var stateStore = new StubStateStore { State = BookProcessingState.NotStarted(bookId) };
        var shuffleStore = new StubShuffleStore();
        var service = new InteriorShuffleService(
            new StubScanner(new BookSource(pages.Select(page => new BookAsset(page.Value, BookAssetKind.Interior)))),
            stateStore,
            shuffleStore,
            new FixedSeedSource(42));
        return new Fixture(new DiscoveredBook("book-one", bookId, directory, workspace), pages, stateStore, shuffleStore, service);
    }

    private sealed record Fixture(
        DiscoveredBook Book,
        IReadOnlyList<FileReference> Pages,
        StubStateStore StateStore,
        StubShuffleStore ShuffleStore,
        InteriorShuffleService Service);

    private sealed class StubScanner(BookSource source) : IBookSourceScanner
    {
        public ValueTask<BookSourceScanResult> ScanAsync(BookId bookId, DirectoryReference bookDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BookSourceScanResult.Succeeded(source));
    }

    private sealed class StubStateStore : IBookWorkspaceStateStore
    {
        public BookProcessingState? State { get; set; }
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult(State);
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default) { State = state; return ValueTask.CompletedTask; }
        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BookProcessingLogEntry>>([]);
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubShuffleStore : IInteriorShuffleStore
    {
        public InteriorShuffleMap? Map { get; private set; }
        public bool ThrowOnNextLoad { get; set; }
        public ValueTask<InteriorShuffleMap?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
        {
            if (ThrowOnNextLoad)
            {
                ThrowOnNextLoad = false;
                return ValueTask.FromException<InteriorShuffleMap?>(new InvalidDataException("invalid shuffle"));
            }
            return ValueTask.FromResult(Map);
        }
        public ValueTask SaveAsync(BookWorkspace workspace, InteriorShuffleMap shuffleMap, CancellationToken cancellationToken = default) { Map = shuffleMap; return ValueTask.CompletedTask; }
    }

    private sealed class FixedSeedSource(int seed) : IInteriorShuffleSeedSource
    {
        public int Create() => seed;
    }
}
