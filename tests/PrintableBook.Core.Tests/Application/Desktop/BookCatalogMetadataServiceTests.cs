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

        await service.SaveBookMetadataAsync(book, new BookProductionMetadata(" Coloring Days ", null, " Cute Friends Coloring Book Pages ", null, " Jane Doe "));
        await service.AssignBrandAsync(book, brand);

        Assert.Equal("COLORING DAYS", stateStore.State!.Metadata!.Title);
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
    public async Task SaveBookMetadata_rejects_all_errors_before_state_access_and_preserves_existing_state()
    {
        var original = BookProcessingState.NotStarted(new BookId("book")) with
        {
            Metadata = BookProductionMetadata.Create("Old Title", null, "Old subcover stays here now", null, "Jane Doe"),
            AssignedBrand = "Brand A"
        };
        var stateStore = new StateStore(original);
        var service = new BookCatalogMetadataService(stateStore, new BrandStore());

        var exception = await Assert.ThrowsAsync<BookCatalogMetadataException>(() => service.SaveBookMetadataAsync(
            Book(),
            new BookProductionMetadata("friend friends friend extra", "and and", "only three terms", null, "Jane Doe")).AsTask());

        Assert.Equal("invalid_book_metadata", exception.Code);
        Assert.Collection(
            exception.ValidationErrors!,
            error => AssertError(error, "title", "term_count"),
            error => AssertDuplicate(error, "title", "friend", "friends"),
            error => AssertDuplicate(error, "title", "friend", "friend"),
            error => AssertDuplicate(error, "subtitle", "and", "and"),
            error => AssertError(error, "subcover", "term_count"));
        Assert.Equal(0, stateStore.Loads);
        Assert.Equal(0, stateStore.Saves);
        Assert.Same(original, stateStore.State);
        Assert.Equal("Brand A", stateStore.State!.AssignedBrand);
    }

    [Theory]
    [InlineData("Peaceful Days", null, "Soft Calm Coloring Pages")]
    [InlineData("Peaceful Days", null, "Soft Calm Coloring Pages Inside")]
    [InlineData("Peaceful Days", null, "Soft Calm Coloring Pages Inside Today")]
    [InlineData(null, null, null)]
    [InlineData("  ", "\t", " ")]
    public async Task SaveBookMetadata_accepts_valid_or_empty_optional_fields(string? title, string? subtitle, string? subcover)
    {
        var stateStore = new StateStore();
        var service = new BookCatalogMetadataService(stateStore, new BrandStore());

        await service.SaveBookMetadataAsync(Book(), new(title, subtitle, subcover, null, null));

        Assert.Equal(1, stateStore.Loads);
        Assert.Equal(1, stateStore.Saves);
    }

    [Fact]
    public async Task SaveKeywordBuilder_preserves_existing_metadata_and_returns_the_persisted_result()
    {
        var original = BookProcessingState.NotStarted(new BookId("book")) with
        {
            Metadata = BookProductionMetadata.Create("Peaceful Days", null, null, null, "Jane Doe")
        };
        var stateStore = new StateStore(original);
        var service = new BookCatalogMetadataService(
            stateStore,
            new BrandStore(),
            new BookKeywordBuilder(new NoOpKeywordShuffler()));

        var result = await service.SaveKeywordBuilderAsync(Book(), ["coloring books", "coloring book"], " targets ");

        Assert.Same(result, stateStore.State!.KeywordBuilder);
        Assert.Equal("PEACEFUL DAYS", stateStore.State.Metadata!.Title);
        Assert.Equal("coloring books book", result.Keyword1);
        Assert.Equal("coloring books, coloring book", result.AdsKeyword);
        Assert.Equal("targets", result.AdsAsin);
        Assert.Equal(1, stateStore.Loads);
        Assert.Equal(1, stateStore.Saves);
    }

    [Fact]
    public async Task SaveKeywordBuilder_loads_saved_generic_keywords_before_building()
    {
        var stateStore = new StateStore(BookProcessingState.NotStarted(new BookId("book")));
        var settingsStore = new SettingsStore(GlobalSettings.Default with
        {
            GenericKeywords = ["generic coloring", "books for adults"]
        });
        var service = new BookCatalogMetadataService(
            stateStore,
            new BrandStore(),
            new BookKeywordBuilder(new NoOpKeywordShuffler()),
            settingsStore);

        var result = await service.SaveKeywordBuilderAsync(
            Book(),
            ["Generic Coloring", "cute animals"],
            "B0123");

        Assert.Equal(1, settingsStore.Loads);
        Assert.Equal(["generic coloring", "books for adults"], result.GenericKeywords);
        Assert.Equal("generic coloring books for adults cute animals", result.Keyword1);
        Assert.Equal("generic coloring, books for adults, cute animals", result.AdsKeyword);
    }

    [Fact]
    public async Task SaveKeywordBuilder_validation_failure_does_not_load_or_save_state()
    {
        var stateStore = new StateStore(BookProcessingState.NotStarted(new BookId("book")));
        var service = new BookCatalogMetadataService(stateStore, new BrandStore());

        var exception = await Assert.ThrowsAsync<BookKeywordBuilderValidationException>(() =>
            service.SaveKeywordBuilderAsync(Book(), [new string('a', 51)], null).AsTask());

        Assert.Equal("keyword_word_too_long", exception.Error.Code);
        Assert.Equal(0, stateStore.Loads);
        Assert.Equal(0, stateStore.Saves);
    }

    [Theory]
    [InlineData("only three terms", 3)]
    [InlineData("one two three four five six seven", 7)]
    public void ValidateForSave_rejects_subcover_outside_four_to_six_terms(string subcover, int count)
    {
        var error = Assert.Single(new BookProductionMetadata(null, null, subcover, null, null).ValidateForSave());

        AssertError(error, "subcover", "term_count");
        Assert.Equal($"Subcover must contain 4 to 6 terms (currently {count}).", error.Message);
    }

    [Fact]
    public void Asin_is_optional_normalized_serialized_and_limited_to_one_hundred_characters()
    {
        var oneHundredCharacters = new string('a', 100);
        var metadata = BookProductionMetadata.Create(null, null, null, null, null, $" {oneHundredCharacters} ");

        Assert.Equal(oneHundredCharacters, metadata.Asin);
        Assert.Empty(metadata.ValidateForSave());
        Assert.Null(BookProductionMetadata.Create(null, null, null, null, null, " ").Asin);

        var json = System.Text.Json.JsonSerializer.Serialize(metadata);
        var restored = System.Text.Json.JsonSerializer.Deserialize<BookProductionMetadata>(json);
        Assert.Equal(oneHundredCharacters, restored!.Asin);

        var error = Assert.Single(new BookProductionMetadata(null, null, null, null, null)
        {
            Asin = new string('a', 101)
        }.ValidateForSave());
        AssertError(error, "asin", "character_limit");
        Assert.Equal("ASIN must contain at most 100 characters (currently 101).", error.Message);
    }

    [Fact]
    public void ValidateForSave_counts_graphemes_and_returns_deterministic_field_order()
    {
        var title = string.Concat(Enumerable.Repeat("a\u0301", 120));
        var subcover = $"one\u00A0two\u202Fthree\u3000four {new string('x', 100)}";

        var errors = new BookProductionMetadata(title, "Friend friend, FRIENDS", subcover, null, "Jane\nDoe").ValidateForSave();

        Assert.Collection(
            errors,
            error => AssertError(error, "title", "term_count"),
            error => AssertError(error, "title", "character_limit"),
            error => AssertDuplicate(error, "subtitle", "Friend", "FRIENDS"),
            error => AssertError(error, "subcover", "character_limit"),
            error => AssertError(error, "author", "single_line"));
    }

    [Theory]
    [InlineData("friend friend,")]
    [InlineData("class classes")]
    [InlineData("person people")]
    [InlineData("friend friendly")]
    public void ValidateForSave_does_not_apply_unapproved_linguistic_matching(string title)
    {
        Assert.Empty(new BookProductionMetadata(title, null, null, null, null).ValidateForSave());
    }

    [Fact]
    public void Execution_policy_requires_assignment_and_blocks_invalid_or_mismatched_assignments()
    {
        Assert.Equal("book_brand_assignment_required", BookBrandExecutionPolicy.Evaluate(null, BookBrandAssignmentStatus.Unassigned, null).Code);
        Assert.True(BookBrandExecutionPolicy.Evaluate("Brand A", BookBrandAssignmentStatus.Valid, "Brand A").IsAllowed);
        Assert.True(BookBrandExecutionPolicy.Evaluate("Brand A", BookBrandAssignmentStatus.Valid, null).IsAllowed);
        Assert.Equal("book_brand_mismatch", BookBrandExecutionPolicy.Evaluate("Brand A", BookBrandAssignmentStatus.Valid, "Brand B").Code);
        Assert.Equal("book_brand_assignment_invalid", BookBrandExecutionPolicy.Evaluate("Brand A", BookBrandAssignmentStatus.AuthorMismatch, "Brand A").Code);
    }

    private static DiscoveredBook Book()
    {
        var id = new BookId("book");
        return new("Book", id, new DirectoryReference("sources/Book"), new BookWorkspace(id, new DirectoryReference("work"), new DirectoryReference("processed"), new DirectoryReference("temp")));
    }

    private static DiscoveredBrand Brand(string name) => new(name, new DirectoryReference($"brands/{name}"));

    private static void AssertError(BookMetadataValidationError error, string field, string code)
    {
        Assert.Equal(field, error.Field);
        Assert.Equal(code, error.Code);
    }

    private static void AssertDuplicate(BookMetadataValidationError error, string field, string left, string right)
    {
        AssertError(error, field, "duplicate_terms");
        Assert.Equal([left, right], error.Tokens);
    }

    private sealed class StateStore(BookProcessingState? initialState = null) : IBookWorkspaceStateStore
    {
        public BookProcessingState? State { get; private set; } = initialState;
        public int Loads { get; private set; }
        public int Saves { get; private set; }
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) { Loads++; return ValueTask.FromResult(State); }
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default) { Saves++; State = state; return ValueTask.CompletedTask; }
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

    private sealed class SettingsStore(GlobalSettings settings) : IGlobalSettingsStore
    {
        public int Loads { get; private set; }

        public ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            Loads++;
            return ValueTask.FromResult(settings);
        }

        public ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default) =>
            LoadAsync(cancellationToken);

        public ValueTask SaveAsync(GlobalSettings value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class NoOpKeywordShuffler : IKeywordOutputShuffler
    {
        public void Shuffle(IList<string> words) { }
    }
}
