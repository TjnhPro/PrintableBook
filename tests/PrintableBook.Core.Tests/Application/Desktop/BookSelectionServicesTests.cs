using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application.Desktop;

public sealed class BookSelectionServicesTests
{
    [Fact]
    public async Task Cover_selection_persists_only_a_snapshot_authorized_cover()
    {
        var store = new RecordingWorkspaceStateStore();
        var book = CreateBook();
        var service = new BookCoverSelectionService(store);

        await service.SelectAsync(book, "Book cover/selected.png", [new BookAsset("Book cover/selected.png", BookAssetKind.Cover)]);

        Assert.Equal("Book cover/selected.png", store.Saved!.SelectedCoverReference);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SelectAsync(book, "outside.png", [new BookAsset("Book cover/selected.png", BookAssetKind.Cover)]).AsTask());
        Assert.DoesNotContain(typeof(BookCoverSelectionService).GetConstructors().Single().GetParameters(), parameter => parameter.ParameterType == typeof(IApplicationRootDiscovery));
        Assert.DoesNotContain(typeof(BookCoverSelectionService).GetConstructors().Single().GetParameters(), parameter => parameter.ParameterType == typeof(IBookSourceScanner));
    }

    [Fact]
    public async Task Interior_frame_mode_persists_the_snapshot_authorized_source_identity()
    {
        var store = new RecordingWorkspaceStateStore();
        var book = CreateBook();
        var source = new FileReference("Book interior/page-001.png");
        var service = new InteriorFrameModeService(store);

        await service.SetAsync(book, source, FrameMode.Enabled);

        Assert.Equal(FrameMode.Enabled, store.Saved!.GetInteriorFrameMode(InteriorSourceKey.FromBookRoot(book.Directory, source)));
        Assert.DoesNotContain(typeof(InteriorFrameModeService).GetConstructors().Single().GetParameters(), parameter => parameter.ParameterType == typeof(IApplicationRootDiscovery));
        Assert.DoesNotContain(typeof(InteriorFrameModeService).GetConstructors().Single().GetParameters(), parameter => parameter.ParameterType == typeof(IBookSourceScanner));
    }

    private static DiscoveredBook CreateBook()
    {
        var id = new BookId("book-one");
        return new DiscoveredBook("Book One", id, new DirectoryReference("sources/Book One"), new BookWorkspace(id, new DirectoryReference("workspace"), new DirectoryReference("processed"), new DirectoryReference("temporary")));
    }

    private sealed class RecordingWorkspaceStateStore : IBookWorkspaceStateStore
    {
        public BookProcessingState? Saved { get; private set; }
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<BookProcessingState?>(Saved);
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default) { Saved = state; return ValueTask.CompletedTask; }
        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BookProcessingLogEntry>>([]);
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
