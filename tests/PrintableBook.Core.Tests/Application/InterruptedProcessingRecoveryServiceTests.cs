using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application;

public sealed class InterruptedProcessingRecoveryServiceTests
{
    [Fact]
    public async Task RecoverAsync_converts_only_previously_running_workspaces_without_removing_them()
    {
        var running = CreateBook("running");
        var completed = CreateBook("completed");
        var cancelled = CreateBook("cancelled");
        var store = new InMemoryStateStore(new Dictionary<BookId, BookProcessingState>
        {
            [running.Id] = BookProcessingState.NotStarted(running.Id).Start(DateTimeOffset.UtcNow).BeginStep("interior-pages", DateTimeOffset.UtcNow),
            [completed.Id] = BookProcessingState.NotStarted(completed.Id).Complete(DateTimeOffset.UtcNow),
            [cancelled.Id] = BookProcessingState.NotStarted(cancelled.Id).Cancel(DateTimeOffset.UtcNow)
        });
        var discovery = new StaticDiscovery(CreateDiscovery([running, completed, cancelled, CreateBook("missing")]));

        await new InterruptedProcessingRecoveryService(discovery, store).RecoverAsync();

        Assert.Equal(BookProcessingStatus.Interrupted, store.States[running.Id].Status);
        Assert.True(store.States[running.Id].MayResume);
        Assert.Equal("interior-pages", store.States[running.Id].CurrentStep);
        Assert.Equal(BookProcessingStatus.Completed, store.States[completed.Id].Status);
        Assert.Equal(BookProcessingStatus.Cancelled, store.States[cancelled.Id].Status);
        Assert.DoesNotContain(new BookId("missing"), store.States.Keys);
        Assert.Equal([running.Id], store.SavedBookIds);
    }

    private static DiscoveredBook CreateBook(string id)
    {
        var bookId = new BookId(id);
        var directory = new DirectoryReference($"C:\\test-root\\{id}");
        return new DiscoveredBook(id, bookId, directory, new BookWorkspace(bookId, new DirectoryReference($"{directory.Value}\\.workspace"), new DirectoryReference($"{directory.Value}\\.workspace\\processed"), new DirectoryReference($"{directory.Value}\\.workspace\\output-temp")));
    }

    private static ApplicationDiscovery CreateDiscovery(IReadOnlyList<DiscoveredBook> books) => new(
        new ApplicationPaths(new DirectoryReference("C:\\test-root"), new DirectoryReference("C:\\test-root\\brands"), new DirectoryReference("C:\\test-root\\sources"), new FileReference("C:\\test-root\\settings.json")),
        [],
        books);

    private sealed class StaticDiscovery(ApplicationDiscovery discovery) : IApplicationRootDiscovery
    {
        public ValueTask<ApplicationDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(discovery);
    }

    private sealed class InMemoryStateStore(Dictionary<BookId, BookProcessingState> states) : IBookWorkspaceStateStore
    {
        public Dictionary<BookId, BookProcessingState> States { get; } = states;
        public List<BookId> SavedBookIds { get; } = [];

        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult(States.GetValueOrDefault(workspace.BookId));
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default)
        {
            States[workspace.BookId] = state;
            SavedBookIds.Add(workspace.BookId);
            return ValueTask.CompletedTask;
        }

        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BookProcessingLogEntry>>([]);
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
