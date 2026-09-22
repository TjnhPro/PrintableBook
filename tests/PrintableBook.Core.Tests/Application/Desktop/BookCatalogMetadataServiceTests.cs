using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application.Desktop;

public sealed class BookCatalogMetadataServiceTests
{
    [Fact]
    public async Task Save_assign_reassign_and_unassign_preserve_one_book_state()
    {
        var stateStore = new StateStore();
        var brandStore = new BrandStore();
        var service = new BookCatalogMetadataService(stateStore, brandStore);
        var book = Book();
        var brand = Brand("Brand A");
        brandStore.Set(brand, BrandMetadata.Create("jane doe"));

        await service.SaveBookMetadataAsync(book, BookProductionMetadata.Create(" Title ", null, "ABCD", null, " Jane Doe "));
        await service.AssignBrandAsync(book, brand);

        Assert.Equal("Title", stateStore.State!.Metadata!.Title);
        Assert.Equal("Brand A", stateStore.State.AssignedBrand);

        await service.UnassignBrandAsync(book);
        Assert.Null(stateStore.State.AssignedBrand);
        Assert.Equal("Jane Doe", stateStore.State.Metadata.Author);
    }

    [Fact]
    public async Task Assign_rejects_missing_and_mismatched_authors()
    {
        var stateStore = new StateStore();
        var brandStore = new BrandStore();
        var service = new BookCatalogMetadataService(stateStore, brandStore);
        var book = Book();
        var brand = Brand("Brand A");

        var missingBook = await Assert.ThrowsAsync<BookCatalogMetadataException>(() => service.AssignBrandAsync(book, brand).AsTask());
        Assert.Equal("book_author_required", missingBook.Code);

        await service.SaveBookMetadataAsync(book, BookProductionMetadata.Create(null, null, null, null, "Jane"));
        var missingBrand = await Assert.ThrowsAsync<BookCatalogMetadataException>(() => service.AssignBrandAsync(book, brand).AsTask());
        Assert.Equal("brand_author_required", missingBrand.Code);

        brandStore.Set(brand, BrandMetadata.Create("John"));
        var mismatch = await Assert.ThrowsAsync<BookCatalogMetadataException>(() => service.AssignBrandAsync(book, brand).AsTask());
        Assert.Equal("book_brand_author_mismatch", mismatch.Code);
    }

    [Fact]
    public async Task SaveBrandAuthor_normalizes_display_text()
    {
        var brandStore = new BrandStore();
        var service = new BookCatalogMetadataService(new StateStore(), brandStore);
        var brand = Brand("Brand A");

        await service.SaveBrandAuthorAsync(brand, " Jane Doe ");

        Assert.Equal("Jane Doe", (await brandStore.LoadAsync(brand.Directory))!.Author);
    }

    [Fact]
    public void Execution_policy_allows_legacy_books_and_blocks_invalid_or_mismatched_assignments()
    {
        Assert.True(BookBrandExecutionPolicy.Evaluate(null, BookBrandAssignmentStatus.Unassigned, "Brand A").IsAllowed);
        Assert.True(BookBrandExecutionPolicy.Evaluate("Brand A", BookBrandAssignmentStatus.Valid, "Brand A").IsAllowed);
        Assert.Equal("book_brand_mismatch", BookBrandExecutionPolicy.Evaluate("Brand A", BookBrandAssignmentStatus.Valid, "Brand B").Code);
        Assert.Equal("book_brand_assignment_invalid", BookBrandExecutionPolicy.Evaluate("Brand A", BookBrandAssignmentStatus.AuthorMismatch, "Brand A").Code);
    }

    private static DiscoveredBook Book()
    {
        var id = new BookId("book");
        return new("Book", id, new DirectoryReference("sources/Book"), new BookWorkspace(id, new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("temp")));
    }

    private static DiscoveredBrand Brand(string name) => new(name, new DirectoryReference($"brands/{name}"));

    private sealed class StateStore : IBookWorkspaceStateStore
    {
        public BookProcessingState? State { get; private set; }
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult(State);
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default) { State = state; return ValueTask.CompletedTask; }
        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BookProcessingLogEntry>>([]);
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class BrandStore : IBrandMetadataStore
    {
        private readonly Dictionary<string, BrandMetadata> values = new(StringComparer.Ordinal);
        public void Set(DiscoveredBrand brand, BrandMetadata metadata) => values[brand.Directory.Value] = metadata;
        public ValueTask<BrandMetadata?> LoadAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(values.GetValueOrDefault(brandDirectory.Value));
        public ValueTask SaveAsync(DirectoryReference brandDirectory, BrandMetadata metadata, CancellationToken cancellationToken = default)
        {
            values[brandDirectory.Value] = metadata;
            return ValueTask.CompletedTask;
        }
    }
}
