using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Tests.Domain;

public sealed class BookKeywordBuilderTests
{
    [Fact]
    public void Build_normalizes_phrases_and_deduplicates_words_case_insensitively()
    {
        var shuffler = new ReversingShuffler();
        var result = new BookKeywordBuilder(shuffler).Build(
            [" coloring\tbooks for adults ", "adult Coloring book", "coloring book"],
            "  B0123\nB0456  ",
            "build-1",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(["coloring books for adults", "adult Coloring book", "coloring book"], result.SourceKeywords);
        Assert.Equal("book adult adults for books coloring", result.Keyword1);
        Assert.Null(result.Keyword2);
        Assert.Equal("coloring books for adults, adult Coloring book, coloring book", result.AdsKeyword);
        Assert.Equal("B0123\nB0456", result.AdsAsin);
        Assert.Equal(1, shuffler.Calls);
    }

    [Fact]
    public void Build_preserves_singular_plural_and_punctuation_as_distinct_words()
    {
        var result = new BookKeywordBuilder(new NoOpShuffler()).Build(
            ["friend friends friend,"],
            null,
            "build-1",
            DateTimeOffset.UnixEpoch);

        Assert.Equal("friend friends friend,", result.Keyword1);
    }

    [Fact]
    public void Build_accepts_exactly_fifty_graphemes()
    {
        var word = new string('a', 50);

        var result = new BookKeywordBuilder(new NoOpShuffler()).Build([word], null, "build-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(word, result.Keyword1);
    }

    [Fact]
    public void Build_rejects_word_over_fifty_without_shuffling()
    {
        var shuffler = new ReversingShuffler();

        var exception = Assert.Throws<BookKeywordBuilderValidationException>(() =>
            new BookKeywordBuilder(shuffler).Build([new string('a', 51)], null, "build-1", DateTimeOffset.UnixEpoch));

        Assert.Equal("keyword_word_too_long", exception.Error.Code);
        Assert.Equal(51, exception.Error.GraphemeCount);
        Assert.Equal(0, shuffler.Calls);
    }

    [Fact]
    public void Build_rejects_the_first_word_requiring_an_eighth_sequential_slot()
    {
        var words = Enumerable.Range(0, 8)
            .Select(index => ((char)('a' + index)) + new string((char)('a' + index), 49))
            .ToArray();
        var shuffler = new ReversingShuffler();

        var exception = Assert.Throws<BookKeywordBuilderValidationException>(() =>
            new BookKeywordBuilder(shuffler).Build([string.Join(' ', words)], null, "build-1", DateTimeOffset.UnixEpoch));

        Assert.Equal("keyword_capacity_exceeded", exception.Error.Code);
        Assert.Equal(words[7], exception.Error.OffendingWord);
        Assert.Equal("sequential_next_fit", exception.Error.PackingRule);
        Assert.Equal(0, shuffler.Calls);
    }

    [Fact]
    public void Build_empty_source_clears_outputs_but_keeps_ads_asin()
    {
        var result = new BookKeywordBuilder(new NoOpShuffler()).Build([" ", "\t"], " target ", "build-1", DateTimeOffset.UnixEpoch);

        Assert.Empty(result.SourceKeywords);
        Assert.All(result.Keywords, Assert.Null);
        Assert.Null(result.AdsKeyword);
        Assert.Equal("target", result.AdsAsin);
    }

    private sealed class NoOpShuffler : IKeywordWordShuffler
    {
        public void Shuffle(IList<string> words) { }
    }

    private sealed class ReversingShuffler : IKeywordWordShuffler
    {
        public int Calls { get; private set; }
        public void Shuffle(IList<string> words)
        {
            Calls++;
            for (var left = 0; left < words.Count / 2; left++)
            {
                var right = words.Count - left - 1;
                (words[left], words[right]) = (words[right], words[left]);
            }
        }
    }
}
