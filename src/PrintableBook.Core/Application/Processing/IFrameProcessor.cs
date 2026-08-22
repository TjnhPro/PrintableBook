namespace PrintableBook.Core.Application.Processing;

public interface IFrameProcessor
{
    ValueTask ApplyAsync(FrameOverlayRequest request, CancellationToken cancellationToken = default);
}
