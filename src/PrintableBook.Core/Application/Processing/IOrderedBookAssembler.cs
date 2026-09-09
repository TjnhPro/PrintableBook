using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record OrderedBookAssemblyRequest(
    BookWorkspace Workspace,
    IReadOnlyList<FileReference> IntroPages,
    IReadOnlyList<InteriorPageProcessingResult> InteriorPages,
    InteriorShuffleMap ShuffleMap,
    ImageSize ExpectedInteriorSize,
    FileReference? BackgroundPage = null);

public sealed record OrderedBookAssembly(
    IReadOnlyList<FileReference> IntroPages,
    IReadOnlyList<FileReference> OrderedInteriorPages,
    FileReference? BackgroundPage)
{
    public int OutputPageCount =>
        (IntroPages.Count + OrderedInteriorPages.Count) *
        (BackgroundPage is null ? 1 : 2);
}

/// <summary>
/// Produces an immutable export order without renaming or changing page raster files.
/// </summary>
public interface IOrderedBookAssembler
{
    ValueTask<OrderedBookAssembly> AssembleAsync(
        OrderedBookAssemblyRequest request,
        CancellationToken cancellationToken = default);
}
