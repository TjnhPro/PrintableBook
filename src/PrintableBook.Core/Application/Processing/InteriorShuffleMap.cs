using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Associates each source interior page with its stable position in an assembled book.
/// </summary>
public sealed record InteriorShuffleEntry(FileReference Page, int OutputIndex);

public sealed record InteriorShuffleMap(IReadOnlyList<InteriorShuffleEntry> Entries, int? Seed);

public static class InteriorShuffleIndexGenerator
{
    public static InteriorShuffleMap Generate(IReadOnlyList<FileReference> pages, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            throw new ArgumentException("At least one interior page is required.", nameof(pages));
        }

        if (pages.Distinct().Count() != pages.Count)
        {
            throw new ArgumentException("Interior page references must be unique.", nameof(pages));
        }

        var shuffledPages = pages.ToArray();
        var random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        for (var index = shuffledPages.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (shuffledPages[index], shuffledPages[swapIndex]) = (shuffledPages[swapIndex], shuffledPages[index]);
        }

        var entries = shuffledPages
            .Select((page, index) => new InteriorShuffleEntry(page, index + 1))
            .ToArray();
        return new InteriorShuffleMap(entries, seed);
    }
}
