using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace PrintableBook.Core.Domain.Books;

public sealed record BookKeywordBuilderState(
    IReadOnlyList<string> SourceKeywords,
    [property: JsonPropertyName("keyword_1")] string? Keyword1,
    [property: JsonPropertyName("keyword_2")] string? Keyword2,
    [property: JsonPropertyName("keyword_3")] string? Keyword3,
    [property: JsonPropertyName("keyword_4")] string? Keyword4,
    [property: JsonPropertyName("keyword_5")] string? Keyword5,
    [property: JsonPropertyName("keyword_6")] string? Keyword6,
    [property: JsonPropertyName("keyword_7")] string? Keyword7,
    string? AdsKeyword,
    string? AdsAsin,
    string BuildId,
    DateTimeOffset BuiltAtUtc,
    int AlgorithmVersion,
    IReadOnlyList<string>? GenericKeywords = null,
    int OmittedWordCount = 0,
    string? ShuffleSeed = null,
    string? InputFingerprint = null,
    string? OutputDigest = null)
{
    public IReadOnlyList<string?> Keywords => [Keyword1, Keyword2, Keyword3, Keyword4, Keyword5, Keyword6, Keyword7];

    public BookKeywordBuilderState NormalizeStored() => this with
    {
        SourceKeywords = BookTextPolicy.NormalizePhrases(SourceKeywords),
        GenericKeywords = BookTextPolicy.NormalizePhrases(GenericKeywords ?? [], distinct: true),
        Keyword1 = NormalizeSlot(Keyword1),
        Keyword2 = NormalizeSlot(Keyword2),
        Keyword3 = NormalizeSlot(Keyword3),
        Keyword4 = NormalizeSlot(Keyword4),
        Keyword5 = NormalizeSlot(Keyword5),
        Keyword6 = NormalizeSlot(Keyword6),
        Keyword7 = NormalizeSlot(Keyword7),
        AdsKeyword = NormalizeSlot(AdsKeyword),
        AdsAsin = BookTextPolicy.NormalizeOptionalMultiline(AdsAsin),
        BuildId = BuildId.Trim(),
        ShuffleSeed = NormalizeSlot(ShuffleSeed),
        InputFingerprint = NormalizeSlot(InputFingerprint),
        OutputDigest = NormalizeSlot(OutputDigest)
    };

    private static string? NormalizeSlot(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public sealed record BookKeywordBuilderValidationError(
    string Code,
    string Message,
    string? OffendingWord = null,
    int? GraphemeCount = null,
    int? MaximumCharacters = null,
    int? UniqueWordCount = null,
    int? RequiredSlotCount = null,
    int? MaximumSlotCount = null,
    string? PackingRule = null);

public sealed class BookKeywordBuilderValidationException(BookKeywordBuilderValidationError error)
    : InvalidOperationException(error.Message)
{
    public BookKeywordBuilderValidationError Error { get; } = error;
}

public interface IKeywordOutputShuffler
{
    void Shuffle(IList<string> values);
}

public sealed class RandomKeywordOutputShuffler : IKeywordOutputShuffler
{
    public void Shuffle(IList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}

public sealed class BookKeywordBuilder(IKeywordOutputShuffler? shuffler = null)
{
    public const int MaximumKeywordCharacters = 50;
    public const int MaximumKeywordSlots = 7;
    public const int MaximumAdsKeywordPhrases = 30;
    public const int PreferredGenericAdsKeywordPhrases = 20;
    public const int PreferredBookAdsKeywordPhrases = 10;
    public const int MaximumSourcePhrases = 500;
    public const int MaximumSourcePhraseCharacters = 10_000;
    public const int MaximumSourceUtf8Bytes = 64 * 1024;
    public const int MaximumNormalizedWords = 10_000;
    public const int CurrentAlgorithmVersion = 4;

    private readonly IKeywordOutputShuffler? compatibilityShuffler = shuffler;

    public BookKeywordBuilderState Build(
        IReadOnlyList<string> sourceKeywords,
        string? adsAsin,
        string buildId,
        DateTimeOffset builtAtUtc) =>
        Build([], sourceKeywords, adsAsin, buildId, builtAtUtc, KeywordShuffleSeed.Create());

    public BookKeywordBuilderState Build(
        IReadOnlyList<string> genericKeywords,
        IReadOnlyList<string> bookKeywords,
        string? adsAsin,
        string buildId,
        DateTimeOffset builtAtUtc) =>
        Build(genericKeywords, bookKeywords, adsAsin, buildId, builtAtUtc, KeywordShuffleSeed.Create());

    public BookKeywordBuilderState Build(
        IReadOnlyList<string> genericKeywords,
        IReadOnlyList<string> bookKeywords,
        string? adsAsin,
        string buildId,
        DateTimeOffset builtAtUtc,
        KeywordShuffleSeed seed)
    {
        ArgumentNullException.ThrowIfNull(genericKeywords);
        ArgumentNullException.ThrowIfNull(bookKeywords);
        if (string.IsNullOrWhiteSpace(buildId)) throw new ArgumentException("A build id is required.", nameof(buildId));

        var normalizedGeneric = BookTextPolicy.NormalizePhrases(genericKeywords, distinct: true);
        var normalizedBookSource = BookTextPolicy.NormalizePhrases(bookKeywords);
        ValidateSourceBounds(normalizedBookSource, normalizedGeneric);
        var canonicalAdsAsin = AdsAsinPolicy.Join(AdsAsinPolicy.Normalize(adsAsin));
        var genericSet = normalizedGeneric.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effectiveBook = BookTextPolicy.NormalizePhrases(normalizedBookSource, distinct: true)
            .Where(phrase => !genericSet.Contains(phrase))
            .ToArray();
        var uniqueWords = OrderedUniqueWords(normalizedGeneric.Concat(effectiveBook));
        ValidateWords(uniqueWords);
        var (slots, omittedWordCount) = Pack(uniqueWords);

        for (var index = 0; index < slots.Count; index++) ShuffleIfNeeded(slots[index], seed, $"slot/{index}");

        var values = slots.Select(slot => string.Join(' ', slot)).Cast<string?>().ToList();
        while (values.Count < MaximumKeywordSlots) values.Add(null);

        var adsKeyword = BuildAdsKeyword(normalizedGeneric, effectiveBook, seed);
        var shuffledAdsAsin = BuildAdsAsin(canonicalAdsAsin, seed);
        var inputFingerprint = KeywordBuilderFingerprint.Input(normalizedGeneric, normalizedBookSource, canonicalAdsAsin);
        var outputDigest = KeywordBuilderFingerprint.Output(values, adsKeyword, shuffledAdsAsin);
        return new BookKeywordBuilderState(
            normalizedBookSource,
            values[0], values[1], values[2], values[3], values[4], values[5], values[6],
            adsKeyword,
            shuffledAdsAsin,
            buildId.Trim(),
            builtAtUtc,
            CurrentAlgorithmVersion,
            normalizedGeneric,
            omittedWordCount,
            seed.Value,
            inputFingerprint,
            outputDigest);
    }

    private static void ValidateSourceBounds(IReadOnlyList<string> bookKeywords, IReadOnlyList<string> genericKeywords)
    {
        if (bookKeywords.Count > MaximumSourcePhrases) throw new BookKeywordBuilderValidationException(new(
            "keyword_phrases_too_many", $"Book Keywords accepts at most {MaximumSourcePhrases} phrases."));
        var oversized = bookKeywords.FirstOrDefault(value => BookTextPolicy.GraphemeCount(value) > MaximumSourcePhraseCharacters);
        if (oversized is not null) throw new BookKeywordBuilderValidationException(new(
            "keyword_phrase_too_long", $"Book Keyword '{oversized}' exceeds {MaximumSourcePhraseCharacters} characters."));
        if (System.Text.Encoding.UTF8.GetByteCount(string.Join('\n', bookKeywords)) > MaximumSourceUtf8Bytes) throw new BookKeywordBuilderValidationException(new(
            "keyword_input_too_large", $"Book Keywords accepts at most {MaximumSourceUtf8Bytes} UTF-8 bytes."));
        var wordCount = genericKeywords.Concat(bookKeywords).Sum(value => BookTextPolicy.SplitWords(value).Count);
        if (wordCount > MaximumNormalizedWords) throw new BookKeywordBuilderValidationException(new(
            "keyword_words_too_many", $"Keyword Builder accepts at most {MaximumNormalizedWords} normalized words."));
    }

    private static IReadOnlyList<string> OrderedUniqueWords(IEnumerable<string> phrases)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = new List<string>();
        foreach (var phrase in phrases)
        {
            foreach (var word in BookTextPolicy.SplitWords(phrase))
            {
                if (seen.Add(word)) words.Add(word);
            }
        }
        return words;
    }

    private static void ValidateWords(IReadOnlyList<string> words)
    {
        foreach (var word in words)
        {
            var wordCount = BookTextPolicy.GraphemeCount(word);
            if (wordCount > MaximumKeywordCharacters)
            {
                throw new BookKeywordBuilderValidationException(new(
                    "keyword_word_too_long",
                    $"The word '{word}' contains {wordCount} characters; maximum is {MaximumKeywordCharacters}.",
                    word,
                    wordCount,
                    MaximumKeywordCharacters));
            }
        }
    }

    private static (List<List<string>> Slots, int OmittedWordCount) Pack(IReadOnlyList<string> words)
    {
        var slots = new List<List<string>>();
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            if (slots.Count == 0) slots.Add([]);
            var current = slots[^1];
            var candidate = current.Count == 0 ? word : $"{string.Join(' ', current)} {word}";
            if (BookTextPolicy.GraphemeCount(candidate) <= MaximumKeywordCharacters)
            {
                current.Add(word);
                continue;
            }

            if (slots.Count == MaximumKeywordSlots)
            {
                return (slots, words.Count - index);
            }

            slots.Add([word]);
        }
        return (slots, 0);
    }

    private string? BuildAdsKeyword(IReadOnlyList<string> genericKeywords, IReadOnlyList<string> bookKeywords, KeywordShuffleSeed seed)
    {
        var genericCount = Math.Min(PreferredGenericAdsKeywordPhrases, genericKeywords.Count);
        var bookCount = Math.Min(PreferredBookAdsKeywordPhrases, bookKeywords.Count);
        var remaining = MaximumAdsKeywordPhrases - genericCount - bookCount;

        var additionalGeneric = Math.Min(remaining, genericKeywords.Count - genericCount);
        genericCount += additionalGeneric;
        remaining -= additionalGeneric;
        bookCount += Math.Min(remaining, bookKeywords.Count - bookCount);

        var selected = genericKeywords.Take(genericCount).Concat(bookKeywords.Take(bookCount)).ToList();
        ShuffleIfNeeded(selected, seed, "ads-keyword");
        return selected.Count == 0 ? null : string.Join(", ", selected);
    }

    private string? BuildAdsAsin(string? adsAsin, KeywordShuffleSeed seed)
    {
        var targets = AdsAsinPolicy.Normalize(adsAsin).ToList();
        ShuffleIfNeeded(targets, seed, "ads-asin");
        return AdsAsinPolicy.Join(targets);
    }

    private void ShuffleIfNeeded(IList<string> values, KeywordShuffleSeed seed, string label)
    {
        if (values.Count < 2) return;
        if (compatibilityShuffler is not null)
        {
            compatibilityShuffler.Shuffle(values);
            return;
        }
        KeywordShuffleV4.Shuffle(values, seed, label);
    }
}
