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
    int AlgorithmVersion)
{
    public IReadOnlyList<string?> Keywords => [Keyword1, Keyword2, Keyword3, Keyword4, Keyword5, Keyword6, Keyword7];

    public BookKeywordBuilderState NormalizeStored() => this with
    {
        SourceKeywords = SourceKeywords
            .Select(BookTextPolicy.NormalizePhrase)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray(),
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

public interface IKeywordWordShuffler
{
    void Shuffle(IList<string> words);
}

public sealed class RandomKeywordWordShuffler : IKeywordWordShuffler
{
    public void Shuffle(IList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        for (var index = words.Count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (words[index], words[swapIndex]) = (words[swapIndex], words[index]);
        }
    }
}

public sealed class BookKeywordBuilder(IKeywordWordShuffler? shuffler = null)
{
    public const int MaximumKeywordCharacters = 50;
    public const int MaximumKeywordSlots = 7;
    public const int CurrentAlgorithmVersion = 1;

    private readonly IKeywordWordShuffler shuffler = shuffler ?? new RandomKeywordWordShuffler();

    public BookKeywordBuilderState Build(
        IReadOnlyList<string> sourceKeywords,
        string? adsAsin,
        string buildId,
        DateTimeOffset builtAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sourceKeywords);
        if (string.IsNullOrWhiteSpace(buildId)) throw new ArgumentException("A build id is required.", nameof(buildId));

        var normalizedPhrases = sourceKeywords
            .Select(BookTextPolicy.NormalizePhrase)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        var uniqueWords = OrderedUniqueWords(normalizedPhrases);
        var slots = Pack(uniqueWords);

        foreach (var slot in slots.Where(slot => slot.Count > 1)) shuffler.Shuffle(slot);

        var values = slots.Select(slot => string.Join(' ', slot)).Cast<string?>().ToList();
        while (values.Count < MaximumKeywordSlots) values.Add(null);

        return new BookKeywordBuilderState(
            normalizedPhrases,
            values[0], values[1], values[2], values[3], values[4], values[5], values[6],
            normalizedPhrases.Length == 0 ? null : string.Join(", ", normalizedPhrases),
            BookTextPolicy.NormalizeOptionalMultiline(adsAsin),
            buildId.Trim(),
            builtAtUtc,
            CurrentAlgorithmVersion);
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

    private static List<List<string>> Pack(IReadOnlyList<string> words)
    {
        var slots = new List<List<string>>();
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
                throw new BookKeywordBuilderValidationException(new(
                    "keyword_capacity_exceeded",
                    "The ordered keyword stream needs an eighth slot under the sequential next-fit packing rule.",
                    OffendingWord: word,
                    UniqueWordCount: words.Count,
                    RequiredSlotCount: MaximumKeywordSlots + 1,
                    MaximumSlotCount: MaximumKeywordSlots,
                    PackingRule: "sequential_next_fit"));
            }

            slots.Add([word]);
        }
        return slots;
    }
}
