using System.Collections.Concurrent;
using ImageMagick;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Scanning;

namespace PrintableBook.Infrastructure.Imaging;

/// <summary>Creates bounded in-memory thumbnails only for assets that discovery has allowlisted.</summary>
public sealed class MagickBookAssetPreviewService(
    IApplicationRootDiscovery rootDiscovery,
    IBookSourceScanner sourceScanner) : IBookAssetPreviewService
{
    private const int MaximumSide = 256;
    private const int MaximumEntries = 160;
    private readonly ConcurrentDictionary<string, BookAssetPreview> cache = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<BookAssetPreview?> GetAsync(string bookId, string sourceReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bookId) || string.IsNullOrWhiteSpace(sourceReference)) return null;
        var discovery = await rootDiscovery.DiscoverAsync(cancellationToken);
        var book = discovery.Books.FirstOrDefault(item => string.Equals(item.Id.Value, bookId, StringComparison.Ordinal));
        if (book is null) return null;

        var scan = await sourceScanner.ScanAsync(book.Id, book.Directory, cancellationToken);
        var asset = scan.Source?.Assets.FirstOrDefault(item => string.Equals(item.Reference, sourceReference, StringComparison.OrdinalIgnoreCase));
        if (asset is null) return null;

        var fileInfo = new FileInfo(asset.Reference);
        if (!fileInfo.Exists) return null;
        var key = $"{book.Id.Value}|{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        if (cache.TryGetValue(key, out var cached)) return cached;

        cancellationToken.ThrowIfCancellationRequested();
        using var image = new MagickImage(asset.Reference);
        image.AutoOrient();
        image.Resize(new MagickGeometry(MaximumSide, MaximumSide) { IgnoreAspectRatio = false, Greater = true });
        image.Strip();
        image.Format = MagickFormat.Png;
        var bytes = image.ToByteArray();
        var preview = new BookAssetPreview(asset.Reference, (int)image.Width, (int)image.Height, $"data:image/png;base64,{Convert.ToBase64String(bytes)}");
        if (cache.Count >= MaximumEntries) cache.Clear();
        cache[key] = preview;
        return preview;
    }
}
