namespace PrintableBook.Core.Application.Processing;

public interface IArtworkResizeProcessor
{
    ValueTask ResizeAsync(ArtworkResizeRequest request, CancellationToken cancellationToken = default);
}
