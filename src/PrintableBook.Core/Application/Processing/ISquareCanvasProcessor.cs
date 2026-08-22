namespace PrintableBook.Core.Application.Processing;

public interface ISquareCanvasProcessor
{
    ValueTask NormalizeAsync(SquareCanvasRequest request, CancellationToken cancellationToken = default);
}
