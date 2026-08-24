using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Infrastructure.Discovery;

public sealed class PhysicalBrandFrameResolver(
    IFileSystem fileSystem,
    IImageInspector imageInspector) : IBrandFrameResolver
{
    public async ValueTask<FileReference?> ResolveCompatibleFrameAsync(
        DiscoveredBrand brand,
        ImageSize targetSize,
        CancellationToken cancellationToken = default)
    {
        var candidate = new FileReference(Path.Combine(brand.Directory.Value, "frame.png"));
        if (!await fileSystem.FileExistsAsync(candidate, cancellationToken)) return null;

        try
        {
            return await imageInspector.GetSizeAsync(candidate, cancellationToken) == targetSize
                ? candidate
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
