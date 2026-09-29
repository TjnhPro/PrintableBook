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
    int OmittedWordCount = 0)
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
        BuildId = BuildId.Trim()
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
    public const int CurrentAlgorithmVersion = 3;

    private readonly IKeywordOutputShuffler shuffler = shuffler ?? new RandomKeywordOutputShuffler();

    public BookKeywordBuilderState Build(
        IReadOnlyList<string> sourceKeywords,
        string? adsAsin,
        string buildId,
        DateTimeOffset builtAtUtc) =>
        Build([], sourceKeywords, adsAsin, buildId, builtAtUtc);

    public BookKeywordBuilderState Build(
        IReadOnlyList<string> genericKeywords,
        IReadOnlyList<string> bookKeywords,
        string? adsAsin,
        string buildId,
        DateTimeOffset builtAtUtc)
    {
        ArgumentNullException.ThrowIfNull(genericKeywords);
        ArgumentNullException.ThrowIfNull(bookKeywords);
        if (string.IsNullOrWhiteSpace(buildId)) throw new ArgumentException("A build id is required.", nameof(buildId));

        var normalizedGeneric = BookTextPolicy.NormalizePhrases(genericKeywords, distinct: true);
        var normalizedBookSource = BookTextPolicy.NormalizePhrases(bookKeywords);
        var genericSet = normalizedGeneric.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effectiveBook = BookTextPolicy.NormalizePhrases(normalizedBookSource, distinct: true)
            .Where(phrase => !genericSet.Contains(phrase))
            .ToArray();
        var uniqueWords = OrderedUniqueWords(normalizedGeneric.Concat(effectiveBook));
        ValidateWords(uniqueWords);
        var (slots, omittedWordCount) = Pack(uniqueWords);

        foreach (var slot in slots) ShuffleIfNeeded(slot);

        var values = slots.Select(slot => string.Join(' ', slot)).Cast<string?>().ToList();
        while (values.Count < MaximumKeywordSlots) values.Add(null);

        return new BookKeywordBuilderState(
            normalizedBookSource,
            values[0], values[1], values[2], values[3], values[4], values[5], values[6],
            BuildAdsKeyword(normalizedGeneric, effectiveBook),
            BuildAdsAsin(adsAsin),
            buildId.Trim(),
            builtAtUtc,
            CurrentAlgorithmVersion,
            normalizedGeneric,
            omittedWordCount);
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

    private string? BuildAdsKeyword(IReadOnlyList<string> genericKeywords, IReadOnlyList<string> bookKeywords)
    {
        var genericCount = Math.Min(PreferredGenericAdsKeywordPhrases, genericKeywords.Count);
        var bookCount = Math.Min(PreferredBookAdsKeywordPhrases, bookKeywords.Count);
        var remaining = MaximumAdsKeywordPhrases - genericCount - bookCount;

        var additionalGeneric = Math.Min(remaining, genericKeywords.Count - genericCount);
        genericCount += additionalGeneric;
        remaining -= additionalGeneric;
        bookCount += Math.Min(remaining, bookKeywords.Count - bookCount);

        var selected = genericKeywords.Take(genericCount).Concat(bookKeywords.Take(bookCount)).ToList();
        ShuffleIfNeeded(selected);
        return selected.Count == 0 ? null : string.Join(", ", selected);
    }

    private string? BuildAdsAsin(string? adsAsin)
    {
        var normalized = BookTextPolicy.NormalizeOptionalMultiline(adsAsin);
        if (normalized is null) return null;

        var targets = normalized
            .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(target => target.Length > 0)
            .ToList();
        ShuffleIfNeeded(targets);
        return targets.Count == 0 ? null : string.Join(',', targets);
    }

    private void ShuffleIfNeeded(IList<string> values)
    {
        if (values.Count > 1) shuffler.Shuffle(values);
    }
}
