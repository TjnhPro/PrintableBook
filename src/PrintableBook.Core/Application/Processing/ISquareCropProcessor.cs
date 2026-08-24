using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Center-crops a raster to a square using its smaller side.
/// </summary>
public interface ISquareCropProcessor
{
    ValueTask CropAsync(
        SquareCropRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SquareCropRequest(FileReference Source, FileReference Target);
