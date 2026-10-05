using System.Security.Cryptography;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Application.Scanning;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed record InteriorShuffleDesktopSummary(
    string Status,
    int PageCount,
    int EligiblePageCount,
    bool CanRandomize);

public sealed record InteriorShuffleResult(int PageCount, int? Seed);

public sealed class InteriorShuffleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IInteriorShuffleSeedSource
{
    int Create();
}

public sealed class SecureInteriorShuffleSeedSource : IInteriorShuffleSeedSource
{
    public int Create() => RandomNumberGenerator.GetInt32(int.MaxValue);
}

public interface IInteriorShuffleService
{
    ValueTask<InteriorShuffleResult> RandomizeAsync(DiscoveredBook book, CancellationToken cancellationToken = default);
}

public sealed class InteriorShuffleService(
    IBookSourceScanner sourceScanner,
    IBookWorkspaceStateStore stateStore,
    IInteriorShuffleStore shuffleStore,
    IInteriorShuffleSeedSource seedSource) : IInteriorShuffleService
{
    private const int MaximumRandomAttempts = 32;

    public async ValueTask<InteriorShuffleResult> RandomizeAsync(
        DiscoveredBook book,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        var scan = await sourceScanner.ScanAsync(book.Id, book.Directory, cancellationToken);
        if (!scan.IsSuccess)
        {
            throw new InteriorShuffleException(
                "interior_shuffle_source_invalid",
                scan.Failure?.Message ?? "The Book interior source is unavailable.");
        }

        var validation = BookSourceValidator.Validate(scan.Source!);
        if (!validation.IsSuccess)
        {
            throw new InteriorShuffleException("interior_shuffle_source_invalid", validation.Failure!.Message);
        }

        var state = await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
        var pages = InteriorShufflePolicy.EligiblePages(validation.Source, book.Directory, state);
        if (pages.Count == 0)
        {
            throw new InteriorShuffleException(
                "interior_shuffle_no_active_pages",
                "Activate at least one Interior page before randomizing.");
        }

        InteriorShuffleMap? current;
        try
        {
            current = await shuffleStore.LoadAsync(book.Workspace, cancellationToken);
        }
        catch (InvalidDataException)
        {
            current = null;
        }
        InteriorShuffleMap next;
        var attempts = 0;
        do
        {
            next = InteriorShuffleIndexGenerator.Generate(pages, seedSource.Create());
            attempts++;
        }
        while (pages.Count > 1 &&
               current is not null &&
               InteriorShufflePolicy.Compatibility(current, pages) == InteriorShuffleCompatibility.Current &&
               InteriorShufflePolicy.HasSameOrder(current, next) &&
               attempts < MaximumRandomAttempts);

        if (pages.Count > 1 && current is not null &&
            InteriorShufflePolicy.Compatibility(current, pages) == InteriorShuffleCompatibility.Current &&
            InteriorShufflePolicy.HasSameOrder(current, next))
        {
            var currentOrder = current.Entries.OrderBy(entry => entry.OutputIndex).Select(entry => entry.Page).ToArray();
            var rotated = currentOrder.Skip(1).Concat(currentOrder.Take(1)).ToArray();
            next = new InteriorShuffleMap(
                rotated.Select((page, index) => new InteriorShuffleEntry(page, index + 1)).ToArray(),
                next.Seed);
        }

        await shuffleStore.SaveAsync(book.Workspace, next, cancellationToken);
        return new InteriorShuffleResult(next.Entries.Count, next.Seed);
    }
}
