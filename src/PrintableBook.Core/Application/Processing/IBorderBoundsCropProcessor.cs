using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Removes a detected source frame by retaining only pixels strictly inside its bounds.
/// </summary>
public interface IBorderBoundsCropProcessor
{
    ValueTask CropAsync(
        BorderBoundsCropRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BorderBoundsCropRequest(
    FileReference Source,
    FileReference Target,
    ImageRectangle BorderBounds);
