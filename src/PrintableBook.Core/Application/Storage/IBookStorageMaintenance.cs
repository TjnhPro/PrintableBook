using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Storage;

public interface IBookStorageMaintenance
{
    ValueTask<long> GetBookSizeBytesAsync(
        DirectoryReference bookDirectory,
        CancellationToken cancellationToken = default);

    ValueTask<long> ClearHeavyProcessingCacheAsync(
        BookWorkspace workspace,
        CancellationToken cancellationToken = default);
}

public sealed class BookStorageCleanupException(long freedBytes, Exception innerException)
    : IOException("One or more cache files could not be removed.", innerException)
{
    public long FreedBytes { get; } = freedBytes;
}
