using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Associates each source interior page with its stable position in an assembled book.
/// </summary>
public sealed record InteriorShuffleEntry(FileReference Page, int OutputIndex);

public sealed record InteriorShuffleMap(IReadOnlyList<InteriorShuffleEntry> Entries, int? Seed);

public enum InteriorShuffleCompatibility
{
    Missing,
    Current,
    Stale
}

public static class InteriorShufflePolicy
{
    public static IReadOnlyList<FileReference> EligiblePages(
        BookSource source,
        DirectoryReference bookDirectory,
        BookProcessingState state)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bookDirectory);
        ArgumentNullException.ThrowIfNull(state);

        var introKeys = state.HasIntro
            ? new HashSet<string>(state.SelectedIntroInteriorSourceKeys ?? [], StringComparer.OrdinalIgnoreCase)
            : [];

        return source.GetAssets(BookAssetKind.Interior)
            .Select(asset => new FileReference(asset.Reference))
            .Where(page =>
            {
                var key = InteriorSourceKey.FromBookRoot(bookDirectory, page);
                return !introKeys.Contains(key) && state.IsInteriorActive(key);
            })
            .OrderBy(page => page.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static InteriorShuffleCompatibility Compatibility(
        InteriorShuffleMap? shuffleMap,
        IReadOnlyList<FileReference> eligiblePages)
    {
        ArgumentNullException.ThrowIfNull(eligiblePages);
        if (shuffleMap is null) return InteriorShuffleCompatibility.Missing;
        if (shuffleMap.Entries.Count != eligiblePages.Count ||
            shuffleMap.Entries.Select(entry => entry.Page.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != shuffleMap.Entries.Count ||
            shuffleMap.Entries.Select(entry => entry.OutputIndex).Distinct().Count() != shuffleMap.Entries.Count ||
            !shuffleMap.Entries.Select(entry => entry.OutputIndex).Order().SequenceEqual(Enumerable.Range(1, eligiblePages.Count)) ||
            !shuffleMap.Entries.Select(entry => entry.Page.Value).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(eligiblePages.Select(page => page.Value).OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            return InteriorShuffleCompatibility.Stale;
        }

        return InteriorShuffleCompatibility.Current;
    }

    public static bool HasSameOrder(InteriorShuffleMap left, InteriorShuffleMap right) =>
        left.Entries.OrderBy(entry => entry.OutputIndex).Select(entry => entry.Page.Value)
            .SequenceEqual(
                right.Entries.OrderBy(entry => entry.OutputIndex).Select(entry => entry.Page.Value),
                StringComparer.OrdinalIgnoreCase);
}

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
