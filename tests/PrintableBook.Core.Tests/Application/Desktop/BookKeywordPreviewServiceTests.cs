using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Tests.Application.Desktop;

public sealed class BookKeywordPreviewServiceTests
{
    private static readonly KeywordShuffleSeed Seed = new(Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())
        .TrimEnd('=').Replace('+', '-').Replace('/', '_'));

    [Fact]
    public async Task Shuffle_creates_preview_after_reading_language_without_writing_book_state()
    {
        var stateStore = new StateStore();
        var service = CreateService(stateStore);

        var result = await service.ShuffleAsync(Book(), ["cute animals", "calm coloring"], " B012345678 ");

        Assert.Equal("build-1", result.Preview.BuildId);
        Assert.Equal(Seed.Value, result.Preview.ShuffleSeed);
        Assert.False(string.IsNullOrWhiteSpace(result.Receipt));
        Assert.False(string.IsNullOrWhiteSpace(result.ReceiptDigest));
        Assert.Equal(1, stateStore.Loads);
        Assert.Equal(0, stateStore.Saves);
    }

    [Fact]
    public async Task Shuffle_uses_only_the_profile_for_the_persisted_book_language()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")) with { LanguageCode = "de" };
        var settings = new SettingsStore(SettingsWithProfiles(
            ("en", ["English generic"]),
            ("de", ["German generic"])));
        var service = CreateService(new StateStore(state), settings);

        var result = await service.ShuffleAsync(Book(), ["book phrase"], null);

        Assert.Equal(["German generic"], result.Preview.GenericKeywords);
        Assert.DoesNotContain("English generic", result.Preview.GenericKeywords!);
    }

    [Fact]
    public async Task Shuffle_does_not_fall_back_to_English_when_the_book_language_profile_is_empty()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")) with { LanguageCode = "de" };
        var settings = new SettingsStore(SettingsWithProfiles(("en", ["English generic"])));
        var service = CreateService(new StateStore(state), settings);

        var result = await service.ShuffleAsync(Book(), ["German book phrase"], null);

        Assert.Empty(result.Preview.GenericKeywords!);
        Assert.Contains("German book phrase", result.Preview.SourceKeywords);
    }

    [Fact]
    public async Task Save_persists_the_exact_preview_and_is_idempotent()
    {
        var stateStore = new StateStore();
        var service = CreateService(stateStore);
        var preview = await service.ShuffleAsync(Book(), ["cute animals", "calm coloring"], "B012345678");

        var first = await service.SaveAsync(Book(), preview.Receipt);
        var second = await service.SaveAsync(Book(), preview.Receipt);

        Assert.Equal("saved", first.Disposition);
        Assert.Equal("already_saved", second.Disposition);
        Assert.Equal(preview.Preview.OutputDigest, first.KeywordBuilder.OutputDigest);
        Assert.Equal(preview.Preview.Keywords, first.KeywordBuilder.Keywords);
        Assert.Equal(preview.Preview.OutputDigest, stateStore.State!.KeywordBuilder!.OutputDigest);
        Assert.Equal(1, stateStore.Saves);
    }

    [Fact]
    public async Task Tampered_or_cross_book_receipts_are_rejected()
    {
        var service = CreateService(new StateStore());
        var preview = await service.ShuffleAsync(Book(), ["cute animals"], null);
        var tampered = preview.Receipt[..^1] + (preview.Receipt[^1] == 'A' ? 'B' : 'A');

        var invalid = await Assert.ThrowsAsync<KeywordPreviewException>(() => service.SaveAsync(Book(), tampered).AsTask());
        var crossBook = await Assert.ThrowsAsync<KeywordPreviewException>(() => service.SaveAsync(Book("other"), preview.Receipt).AsTask());

        Assert.Equal("keyword_preview_invalid", invalid.Error.Code);
        Assert.Equal("keyword_preview_invalid", crossBook.Error.Code);
    }

    [Fact]
    public async Task Updating_ads_asin_keeps_keyword_and_ads_keyword_output_unchanged()
    {
        var service = CreateService(new StateStore());
        var original = await service.ShuffleAsync(Book(), ["cute animals", "calm coloring"], "B012345678");

        var updated = await service.UpdateAdsAsinAsync(Book(), original.Receipt, "B087654321");

        Assert.Equal(original.Preview.Keywords, updated.Preview.Keywords);
        Assert.Equal(original.Preview.AdsKeyword, updated.Preview.AdsKeyword);
        Assert.NotEqual(original.Preview.AdsAsin, updated.Preview.AdsAsin);
        Assert.Equal("B087654321", updated.Preview.AdsAsinSource);
    }

    [Fact]
    public async Task Open_saved_reissues_a_receipt_from_persisted_deterministic_metadata()
    {
        var stateStore = new StateStore();
        var firstProcess = CreateService(stateStore, key: 7);
        var preview = await firstProcess.ShuffleAsync(Book(), ["cute animals"], "B012345678");
        await firstProcess.SaveAsync(Book(), preview.Receipt);
        var restartedProcess = CreateService(stateStore, key: 9);

        var reopened = await restartedProcess.OpenSavedAsync(Book(), preview.Preview.BuildId);

        Assert.Equal(preview.Preview.OutputDigest, reopened.Preview.OutputDigest);
        Assert.Equal(preview.Preview.Keywords, reopened.Preview.Keywords);
        await restartedProcess.SaveAsync(Book(), reopened.Receipt);
        var oldReceipt = await Assert.ThrowsAsync<KeywordPreviewException>(() => restartedProcess.SaveAsync(Book(), preview.Receipt).AsTask());
        Assert.Equal("keyword_preview_invalid", oldReceipt.Error.Code);
    }

    [Fact]
    public async Task Changed_generic_keywords_make_an_existing_preview_stale()
    {
        var settings = new SettingsStore(GlobalSettings.Default with { GenericKeywords = ["first generic"] });
        var service = CreateService(new StateStore(), settings: settings);
        var preview = await service.ShuffleAsync(Book(), ["cute animals"], null);
        settings.Value = settings.Value with { GenericKeywords = ["changed generic"] };

        var exception = await Assert.ThrowsAsync<KeywordPreviewException>(() => service.SaveAsync(Book(), preview.Receipt).AsTask());

        Assert.Equal("keyword_preview_stale", exception.Error.Code);
        Assert.Equal("shuffle", exception.Error.RetryAction);
    }

    [Fact]
    public async Task Changing_an_unrelated_language_profile_does_not_make_the_preview_stale()
    {
        var state = BookProcessingState.NotStarted(new BookId("book")) with { LanguageCode = "de" };
        var settings = new SettingsStore(SettingsWithProfiles(
            ("en", ["first English generic"]),
            ("de", ["German generic"])));
        var service = CreateService(new StateStore(state), settings);
        var preview = await service.ShuffleAsync(Book(), ["cute animals"], null);
        settings.Value = SettingsWithProfiles(
            ("en", ["changed English generic"]),
            ("de", ["German generic"]));

        var saved = await service.SaveAsync(Book(), preview.Receipt);

        Assert.Equal("saved", saved.Disposition);
        Assert.Equal(["German generic"], saved.KeywordBuilder.GenericKeywords);
    }

    [Fact]
    public async Task Crawl_source_is_resolved_from_the_signed_preview()
    {
        var service = CreateService(new StateStore());
        var preview = await service.ShuffleAsync(Book(), ["cute animals", "calm coloring"], null);

        var source = await service.ResolveCrawlSourceAsync(Book(), preview.Receipt, null);

        Assert.Equal(preview.ReceiptDigest, source.ReceiptDigest);
        Assert.NotEmpty(source.Keywords);
        Assert.Equal(PrintableBook.Core.Application.AmazonCrawl.AmazonCrawlPolicy.Fingerprint(source.Keywords), source.SourceFingerprint);
    }

    [Fact]
    public async Task Saved_crawl_source_is_rebuilt_and_rejects_changed_generic_keywords()
    {
        var stateStore = new StateStore();
        var settings = new SettingsStore(GlobalSettings.Default with { GenericKeywords = ["first generic"] });
        var service = CreateService(stateStore, settings);
        var preview = await service.ShuffleAsync(Book(), ["cute animals"], null);
        await service.SaveAsync(Book(), preview.Receipt);

        var source = await service.ResolveCrawlSourceAsync(Book(), null, preview.Preview.BuildId);
        settings.Value = settings.Value with { GenericKeywords = ["changed generic"] };
        var stale = await Assert.ThrowsAsync<KeywordPreviewException>(() =>
            service.ResolveCrawlSourceAsync(Book(), null, preview.Preview.BuildId).AsTask());

        Assert.NotEmpty(source.Keywords);
        Assert.Null(source.ReceiptDigest);
        Assert.Equal("keyword_preview_stale", stale.Error.Code);
    }

    private static BookKeywordPreviewService CreateService(
        StateStore stateStore,
        SettingsStore? settings = null,
        byte key = 7) => new(
            stateStore,
            settings ?? new SettingsStore(GlobalSettings.Default with { GenericKeywords = ["generic coloring"] }),
            new KeywordPreviewReceiptProtector(new FixedKeyProvider(key)),
            new FixedSeedSource(),
            new FixedBuildIdFactory(),
            new FixedTimeProvider());

    private static GlobalSettings SettingsWithProfiles(
        params (string LanguageCode, IReadOnlyList<string> Keywords)[] profiles) =>
        GlobalSettings.Default with
        {
            GenericKeywordsByLanguage = profiles.ToDictionary(
                profile => profile.LanguageCode,
                profile => profile.Keywords,
                StringComparer.Ordinal)
        };

    private static DiscoveredBook Book(string id = "book")
    {
        var bookId = new BookId(id);
        return new(id, bookId, new DirectoryReference($"sources/{id}"),
            new BookWorkspace(bookId, new DirectoryReference($"work/{id}"), new DirectoryReference($"processed/{id}"), new DirectoryReference($"temp/{id}")));
    }

    private sealed class FixedSeedSource : IKeywordSeedSource
    {
        public KeywordShuffleSeed Create() => Seed;
    }

    private sealed class FixedBuildIdFactory : IBuildIdFactory
    {
        public string Create() => "build-1";
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedKeyProvider(byte key) : IKeywordReceiptKeyProvider
    {
        private readonly byte[] bytes = Enumerable.Repeat(key, 32).ToArray();
        public ReadOnlyMemory<byte> GetKey() => bytes;
    }

    private sealed class StateStore(BookProcessingState? initial = null) : IBookWorkspaceStateStore
    {
        public BookProcessingState? State { get; private set; } = initial;
        public int Loads { get; private set; }
        public int Saves { get; private set; }
        public ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) { Loads++; return ValueTask.FromResult(State); }
        public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default) { Saves++; State = state; return ValueTask.CompletedTask; }
        public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<BookProcessingLogEntry>>([]);
        public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class SettingsStore(GlobalSettings value) : IGlobalSettingsStore
    {
        public GlobalSettings Value { get; set; } = value;
        public ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Value);
        public ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);
        public ValueTask SaveAsync(GlobalSettings value, CancellationToken cancellationToken = default) { Value = value; return ValueTask.CompletedTask; }
    }
}
