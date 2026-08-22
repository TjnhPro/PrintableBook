namespace PrintableBook.Core.Abstractions;

/// <summary>
/// Reads image facts without leaking a third-party image type into Core.
/// </summary>
public interface IImageInspector
{
    ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default);
}
