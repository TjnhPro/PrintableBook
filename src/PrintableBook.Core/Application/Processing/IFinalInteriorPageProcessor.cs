namespace PrintableBook.Core.Application.Processing;

public interface IFinalInteriorPageProcessor
{
    ValueTask ProduceAsync(FinalInteriorPageRequest request, CancellationToken cancellationToken = default);
}
