using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Core.Application.Desktop;

/// <summary>
/// Resolves a brand frame that can safely be applied to an interior page.
/// </summary>
public interface IBrandFrameResolver
{
    ValueTask<FileReference?> ResolveCompatibleFrameAsync(
        DiscoveredBrand brand,
        ImageSize targetSize,
        CancellationToken cancellationToken = default);
}
