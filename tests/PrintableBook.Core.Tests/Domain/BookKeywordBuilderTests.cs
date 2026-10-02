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
        Assert.Equal("coloring book, adult Coloring book, coloring books for adults", result.AdsKeyword);
        Assert.Equal("B0456,B0123", result.AdsAsin);
        Assert.Equal(3, shuffler.Calls);
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
    public void Build_omits_words_after_seven_slots_and_returns_a_non_blocking_count()
    {
        var words = Enumerable.Range(0, 8)
            .Select(index => ((char)('a' + index)) + new string((char)('a' + index), 49))
            .ToArray();
        var shuffler = new ReversingShuffler();

        var result = new BookKeywordBuilder(shuffler).Build(
            [],
            [string.Join(' ', words)],
            null,
            "build-1",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(words.Take(7), result.Keywords);
        Assert.Equal(1, result.OmittedWordCount);
        Assert.Equal(0, shuffler.Calls);
    }

    [Fact]
    public void Build_prioritizes_generic_phrases_and_filters_matching_book_phrases()
    {
        var result = new BookKeywordBuilder(new NoOpShuffler()).Build(
            [" coloring   books ", "books for adults"],
            ["Coloring Books", "cute animals", "cute animals"],
            " target ",
            "build-1",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(["coloring books", "books for adults"], result.GenericKeywords);
        Assert.Equal(["Coloring Books", "cute animals", "cute animals"], result.SourceKeywords);
        Assert.Equal("coloring books for adults cute animals", result.Keyword1);
        Assert.Equal("coloring books, books for adults, cute animals", result.AdsKeyword);
        Assert.Equal(4, result.AlgorithmVersion);
    }

    [Theory]
    [InlineData(25, 20, 20, 10)]
    [InlineData(15, 30, 15, 15)]
    [InlineData(30, 5, 25, 5)]
    [InlineData(8, 6, 8, 6)]
    public void Build_ads_keyword_uses_flexible_generic_and_book_quotas(
        int availableGeneric,
        int availableBook,
        int expectedGeneric,
        int expectedBook)
    {
        var generic = Enumerable.Range(1, availableGeneric).Select(index => $"generic-{index}").ToArray();
        var book = Enumerable.Range(1, availableBook).Select(index => $"book-{index}").ToArray();

        var result = new BookKeywordBuilder(new NoOpShuffler()).Build(
            generic,
            book,
            null,
            "build-1",
            DateTimeOffset.UnixEpoch);
        var selected = result.AdsKeyword!.Split(", ");

        Assert.Equal(expectedGeneric + expectedBook, selected.Length);
        Assert.Equal(generic.Take(expectedGeneric), selected.Take(expectedGeneric));
        Assert.Equal(book.Take(expectedBook), selected.Skip(expectedGeneric));
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

    [Fact]
    public void Build_randomizes_every_populated_keyword_slot()
    {
        var words = Enumerable.Range(0, 14)
            .Select(index => ((char)('a' + index)) + new string((char)('a' + index), 23))
            .ToArray();
        var shuffler = new ReversingShuffler();

        var result = new BookKeywordBuilder(shuffler).Build(
            [string.Join(' ', words)],
            null,
            "build-1",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(7, result.Keywords.Count(keyword => keyword is not null));
        Assert.Equal(7, shuffler.Calls);
        for (var index = 0; index < 7; index++)
        {
            Assert.Equal($"{words[(index * 2) + 1]} {words[index * 2]}", result.Keywords[index]);
        }
    }

    [Fact]
    public void Build_skips_random_for_empty_or_single_value_outputs()
    {
        var shuffler = new ReversingShuffler();

        var result = new BookKeywordBuilder(shuffler).Build([], " ONLY-ASIN ", "build-1", DateTimeOffset.UnixEpoch);

        Assert.All(result.Keywords, Assert.Null);
        Assert.Null(result.AdsKeyword);
        Assert.Equal("ONLY-ASIN", result.AdsAsin);
        Assert.Equal(0, shuffler.Calls);
    }

    [Fact]
    public void Build_with_the_same_v4_seed_is_reproducible_across_instances()
    {
        var seed = KeywordShuffleSeed.FromBytes(Enumerable.Range(0, 32).Select(Convert.ToByte).ToArray());
        var first = new BookKeywordBuilder().Build(
            ["generic coloring", "books for adults"],
            ["cozy animals", "calm moments"],
            "B000000001,B000000002",
            "build-1",
            DateTimeOffset.UnixEpoch,
            seed);
        var second = new BookKeywordBuilder().Build(
            ["generic coloring", "books for adults"],
            ["cozy animals", "calm moments"],
            "B000000001,B000000002",
            "build-1",
            DateTimeOffset.UnixEpoch,
            seed);

        Assert.Equal(first.Keywords, second.Keywords);
        Assert.Equal(first.AdsKeyword, second.AdsKeyword);
        Assert.Equal(first.AdsAsin, second.AdsAsin);
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        Assert.Equal(first.OutputDigest, second.OutputDigest);
        Assert.Equal(seed.Value, first.ShuffleSeed);
    }

    [Fact]
    public void Build_changing_only_ads_asin_preserves_keyword_outputs_with_the_same_seed()
    {
        var seed = KeywordShuffleSeed.FromBytes(Enumerable.Repeat((byte)7, 32).ToArray());
        var builder = new BookKeywordBuilder();
        var first = builder.Build(["generic one"], ["book one", "book two"], "B000000001", "build-1", DateTimeOffset.UnixEpoch, seed);
        var updated = builder.Build(["generic one"], ["book one", "book two"], "B000000001,B000000002", "build-2", DateTimeOffset.UnixEpoch.AddMinutes(1), seed);

        Assert.Equal(first.Keywords, updated.Keywords);
        Assert.Equal(first.AdsKeyword, updated.AdsKeyword);
        Assert.NotEqual(first.AdsAsin, updated.AdsAsin);
        Assert.NotEqual(first.InputFingerprint, updated.InputFingerprint);
    }

    [Fact]
    public void Ads_asin_policy_stably_deduplicates_and_only_adds_valid_crawl_targets()
    {
        var result = AdsAsinPolicy.MergeCrawlerResults("manual, B000000001,manual", ["b000000001", "B000000002", "invalid"]);

        Assert.Equal(["manual", "B000000001", "B000000002"], result);
    }

    private sealed class NoOpShuffler : IKeywordOutputShuffler
    {
        public void Shuffle(IList<string> words) { }
    }

    private sealed class ReversingShuffler : IKeywordOutputShuffler
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
