using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Processing;

public sealed class OrderedBookAssembler(IFileSystem fileSystem, IImageInspector imageInspector) : IOrderedBookAssembler
{
    public async ValueTask<OrderedBookAssembly> AssembleAsync(
        OrderedBookAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateShuffleMap(request.InteriorPages, request.ShuffleMap);

        foreach (var introPage in request.IntroPages)
        {
            await ValidateReadableAsync(introPage, cancellationToken);
        }

        var finalPagesBySource = request.InteriorPages.ToDictionary(page => page.Source, page => page.FinalPage);
        foreach (var finalPage in finalPagesBySource.Values)
        {
            await ValidateInteriorPageAsync(finalPage, request.ExpectedInteriorSize, cancellationToken);
        }

        var orderedInteriors = request.ShuffleMap.Entries
            .OrderBy(entry => entry.OutputIndex)
            .Select(entry => finalPagesBySource[entry.Page]);
        return new OrderedBookAssembly(request.IntroPages.Concat(orderedInteriors).ToArray());
    }

    private async ValueTask ValidateReadableAsync(FileReference page, CancellationToken cancellationToken)
    {
        if (!await fileSystem.FileExistsAsync(page, cancellationToken))
        {
            throw new FileNotFoundException("An ordered page is missing.", page.Value);
        }

        _ = await imageInspector.GetInfoAsync(page, cancellationToken);
    }

    private async ValueTask ValidateInteriorPageAsync(
        FileReference page,
        ImageSize expectedSize,
        CancellationToken cancellationToken)
    {
        await ValidateReadableAsync(page, cancellationToken);
        var info = await imageInspector.GetInfoAsync(page, cancellationToken);
        if (info.Size != expectedSize)
        {
            throw new InvalidDataException($"Interior page '{page.Value}' does not match the expected raster size.");
        }
    }

    private static void ValidateShuffleMap(
        IReadOnlyList<InteriorPageProcessingResult> interiorPages,
        InteriorShuffleMap shuffleMap)
    {
        if (interiorPages.Select(page => page.Source).Distinct().Count() != interiorPages.Count ||
            shuffleMap.Entries.Select(entry => entry.Page).Distinct().Count() != shuffleMap.Entries.Count ||
            shuffleMap.Entries.Select(entry => entry.OutputIndex).Distinct().Count() != shuffleMap.Entries.Count ||
            !shuffleMap.Entries.Select(entry => entry.OutputIndex).Order().SequenceEqual(Enumerable.Range(1, interiorPages.Count)) ||
            !shuffleMap.Entries.Select(entry => entry.Page).OrderBy(page => page.Value)
                .SequenceEqual(interiorPages.Select(page => page.Source).OrderBy(page => page.Value)))
        {
            throw new InvalidDataException("The interior shuffle map must contain every processed page exactly once.");
        }
    }
}
