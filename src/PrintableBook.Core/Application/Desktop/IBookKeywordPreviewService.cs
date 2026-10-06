using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public interface IKeywordSeedSource
{
    KeywordShuffleSeed Create();
}

public sealed class SecureKeywordSeedSource : IKeywordSeedSource
{
    public KeywordShuffleSeed Create() => KeywordShuffleSeed.Create();
}

public interface IBuildIdFactory
{
    string Create();
}

public sealed class GuidBuildIdFactory : IBuildIdFactory
{
    public string Create() => Guid.NewGuid().ToString("N");
}

public interface IBookKeywordPreviewService
{
    ValueTask<KeywordPreviewResult> ShuffleAsync(DiscoveredBook book, IReadOnlyList<string> bookKeywords, string? adsAsin, CancellationToken cancellationToken = default);
    ValueTask<KeywordPreviewResult> OpenSavedAsync(DiscoveredBook book, string buildId, CancellationToken cancellationToken = default);
    ValueTask<KeywordPreviewResult> UpdateAdsAsinAsync(DiscoveredBook book, string baseReceipt, string? adsAsin, CancellationToken cancellationToken = default);
    ValueTask<KeywordPreviewSaveResult> SaveAsync(DiscoveredBook book, string receipt, CancellationToken cancellationToken = default);
    ValueTask<KeywordCrawlSource> ResolveCrawlSourceAsync(DiscoveredBook book, string? previewReceipt, string? savedBuildId, CancellationToken cancellationToken = default);
}

public sealed class BookKeywordPreviewService(
    IBookWorkspaceStateStore stateStore,
    IGlobalSettingsStore settingsStore,
    IKeywordPreviewReceiptProtector receiptProtector,
    IKeywordSeedSource seedSource,
    IBuildIdFactory buildIdFactory,
    TimeProvider timeProvider,
    BookKeywordBuilder? builder = null) : IBookKeywordPreviewService
{
    private readonly BookKeywordBuilder builder = builder ?? new BookKeywordBuilder();

    public async ValueTask<KeywordPreviewResult> ShuffleAsync(
        DiscoveredBook book,
        IReadOnlyList<string> bookKeywords,
        string? adsAsin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(bookKeywords);
        var state = await LoadStateAsync(book, cancellationToken);
        var generic = await LoadGenericAsync(state, cancellationToken);
        var preview = builder.Build(generic, bookKeywords, adsAsin, buildIdFactory.Create(), timeProvider.GetUtcNow(), seedSource.Create());
        return Issue(book, preview);
    }

    public async ValueTask<KeywordPreviewResult> OpenSavedAsync(
        DiscoveredBook book,
        string buildId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        var saved = (await LoadStateAsync(book, cancellationToken)).KeywordBuilder?.NormalizeStored();
        if (saved is null || !string.Equals(saved.BuildId, buildId, StringComparison.Ordinal)) throw Stale("The saved keyword output changed. Refresh and try again.", "refresh");
        if (saved.AlgorithmVersion != BookKeywordBuilder.CurrentAlgorithmVersion || string.IsNullOrWhiteSpace(saved.ShuffleSeed) ||
            string.IsNullOrWhiteSpace(saved.InputFingerprint) || string.IsNullOrWhiteSpace(saved.OutputDigest) || saved.AdsAsinSource is null && saved.AdsAsin is not null)
        {
            throw new KeywordPreviewException(new("keyword_legacy_shuffle_required", "Shuffle once to update this legacy keyword output.", "preview", "shuffle"));
        }
        var rebuilt = builder.Build(saved.GenericKeywords ?? [], saved.SourceKeywords, saved.AdsAsinSource, saved.BuildId, saved.BuiltAtUtc, new KeywordShuffleSeed(saved.ShuffleSeed));
        EnsureExact(saved.InputFingerprint, saved.OutputDigest, rebuilt);
        return Issue(book, rebuilt);
    }

    public async ValueTask<KeywordPreviewResult> UpdateAdsAsinAsync(
        DiscoveredBook book,
        string baseReceipt,
        string? adsAsin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        var payload = ValidateReceiptForBook(book, baseReceipt);
        var state = await LoadStateAsync(book, cancellationToken);
        var generic = await LoadGenericAsync(state, cancellationToken);
        EnsureGenericCurrent(payload, generic);
        var preview = builder.Build(
            generic,
            payload.BookKeywords,
            adsAsin,
            buildIdFactory.Create(),
            timeProvider.GetUtcNow(),
            new KeywordShuffleSeed(payload.ShuffleSeed));
        return Issue(book, preview);
    }

    public async ValueTask<KeywordPreviewSaveResult> SaveAsync(
        DiscoveredBook book,
        string receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        var payload = ValidateReceiptForBook(book, receipt);
        var state = await LoadStateAsync(book, cancellationToken);
        var generic = await LoadGenericAsync(state, cancellationToken);
        EnsureGenericCurrent(payload, generic);
        var rebuilt = builder.Build(
            generic,
            payload.BookKeywords,
            payload.AdsAsinSource,
            payload.BuildId,
            DateTimeOffset.FromUnixTimeMilliseconds(payload.BuiltAtUnixMilliseconds),
            new KeywordShuffleSeed(payload.ShuffleSeed));
        EnsureExact(payload.InputFingerprint, payload.OutputDigest, rebuilt);

        if (string.Equals(state.KeywordBuilder?.BuildId, rebuilt.BuildId, StringComparison.Ordinal) &&
            string.Equals(state.KeywordBuilder?.OutputDigest, rebuilt.OutputDigest, StringComparison.Ordinal))
        {
            return new(rebuilt, "already_saved");
        }
        await stateStore.SaveAsync(book.Workspace, state with { KeywordBuilder = rebuilt }, cancellationToken);
        return new(rebuilt, "saved");
    }

    public async ValueTask<KeywordCrawlSource> ResolveCrawlSourceAsync(
        DiscoveredBook book,
        string? previewReceipt,
        string? savedBuildId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        BookKeywordBuilderState source;
        string? receiptDigest = null;
        if (!string.IsNullOrWhiteSpace(previewReceipt))
        {
            var payload = ValidateReceiptForBook(book, previewReceipt);
            var state = await LoadStateAsync(book, cancellationToken);
            var generic = await LoadGenericAsync(state, cancellationToken);
            EnsureGenericCurrent(payload, generic);
            source = builder.Build(generic, payload.BookKeywords, payload.AdsAsinSource, payload.BuildId,
                DateTimeOffset.FromUnixTimeMilliseconds(payload.BuiltAtUnixMilliseconds), new KeywordShuffleSeed(payload.ShuffleSeed));
            EnsureExact(payload.InputFingerprint, payload.OutputDigest, source);
            receiptDigest = receiptProtector.Digest(previewReceipt);
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(savedBuildId);
            var state = await LoadStateAsync(book, cancellationToken);
            var saved = state.KeywordBuilder?.NormalizeStored()
                ?? throw Stale("The saved keyword output was not found. Refresh and try again.", "refresh");
            if (!string.Equals(saved.BuildId, savedBuildId, StringComparison.Ordinal)) throw Stale("The saved keyword output changed. Refresh and try again.", "refresh");
            if (saved.AlgorithmVersion != BookKeywordBuilder.CurrentAlgorithmVersion || string.IsNullOrWhiteSpace(saved.ShuffleSeed) ||
                string.IsNullOrWhiteSpace(saved.InputFingerprint) || string.IsNullOrWhiteSpace(saved.OutputDigest) || saved.AdsAsinSource is null && saved.AdsAsin is not null)
            {
                throw new KeywordPreviewException(new("keyword_legacy_shuffle_required", "Shuffle once to update this legacy keyword output.", "preview", "shuffle"));
            }
            var generic = await LoadGenericAsync(state, cancellationToken);
            source = builder.Build(generic, saved.SourceKeywords, saved.AdsAsinSource, saved.BuildId, saved.BuiltAtUtc, new KeywordShuffleSeed(saved.ShuffleSeed));
            EnsureExact(saved.InputFingerprint, saved.OutputDigest, source);
        }
        var adsKeywords = (source.AdsKeyword ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalized = AmazonCrawlPolicy.NormalizeKeywords(adsKeywords);
        return new(normalized, AmazonCrawlPolicy.SourceFingerprint(normalized), receiptDigest);
    }

    private KeywordPreviewResult Issue(DiscoveredBook book, BookKeywordBuilderState preview)
    {
        if (preview.AlgorithmVersion != BookKeywordBuilder.CurrentAlgorithmVersion || preview.ShuffleSeed is null ||
            preview.InputFingerprint is null || preview.OutputDigest is null)
        {
            throw new InvalidOperationException("A v4 keyword preview must contain deterministic metadata.");
        }
        var payload = new KeywordPreviewReceiptPayload(
            KeywordPreviewReceiptProtector.CurrentReceiptVersion,
            book.Id.Value,
            preview.BuildId,
            preview.BuiltAtUtc.ToUnixTimeMilliseconds(),
            preview.AlgorithmVersion,
            preview.ShuffleSeed,
            preview.SourceKeywords,
            preview.AdsAsinSource,
            GenericFingerprint(preview.GenericKeywords ?? []),
            preview.InputFingerprint,
            preview.OutputDigest);
        var receipt = receiptProtector.Protect(payload);
        return new(preview, receipt, receiptProtector.Digest(receipt));
    }

    private KeywordPreviewReceiptPayload ValidateReceiptForBook(DiscoveredBook book, string receipt)
    {
        var payload = receiptProtector.Unprotect(receipt);
        if (!string.Equals(payload.BookId, book.Id.Value, StringComparison.Ordinal)) throw new KeywordPreviewException(new(
            "keyword_preview_invalid", "The keyword preview belongs to another Book. Shuffle again.", "receipt", "shuffle"));
        if (payload.AlgorithmVersion != BookKeywordBuilder.CurrentAlgorithmVersion) throw new KeywordPreviewException(new(
            "keyword_preview_version_unsupported", "This keyword preview was created by an unsupported version. Shuffle again.", "receipt", "shuffle"));
        return payload;
    }

    private static void EnsureGenericCurrent(KeywordPreviewReceiptPayload payload, IReadOnlyList<string> generic)
    {
        if (!string.Equals(payload.GenericFingerprint, GenericFingerprint(generic), StringComparison.Ordinal)) throw Stale(
            "Generic Keywords changed after this preview. Shuffle again.", "shuffle");
    }

    private static void EnsureExact(string expectedInput, string expectedOutput, BookKeywordBuilderState rebuilt)
    {
        if (!string.Equals(expectedInput, rebuilt.InputFingerprint, StringComparison.Ordinal) ||
            !string.Equals(expectedOutput, rebuilt.OutputDigest, StringComparison.Ordinal)) throw Stale(
            "The keyword preview no longer matches its inputs. Shuffle again.", "shuffle");
    }

    private async ValueTask<IReadOnlyList<string>> LoadGenericAsync(
        BookProcessingState state,
        CancellationToken cancellationToken) =>
        (await settingsStore.LoadAsync(cancellationToken)).GetEffectiveGenericKeywords(state.LanguageCode);

    private async ValueTask<BookProcessingState> LoadStateAsync(DiscoveredBook book, CancellationToken cancellationToken) =>
        await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);

    private static string GenericFingerprint(IReadOnlyList<string> generic) =>
        KeywordBuilderFingerprint.Phrases("keyword-builder-generic-v1", BookTextPolicy.NormalizePhrases(generic, distinct: true));

    private static KeywordPreviewException Stale(string message, string retryAction) => new(new(
        "keyword_preview_stale", message, "preview", retryAction));
}
