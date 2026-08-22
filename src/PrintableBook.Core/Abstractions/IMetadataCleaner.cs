namespace PrintableBook.Core.Abstractions;

public interface IMetadataCleaner
{
    ValueTask CleanAsync(FileReference file, CancellationToken cancellationToken = default);
}
