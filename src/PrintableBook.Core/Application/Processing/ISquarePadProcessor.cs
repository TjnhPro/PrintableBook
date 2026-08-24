using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Centers a raster on an opaque white square using its larger side.
/// </summary>
public interface ISquarePadProcessor
{
    ValueTask PadAsync(
        SquarePadRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SquarePadRequest(FileReference Source, FileReference Target);
