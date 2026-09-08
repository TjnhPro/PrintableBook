namespace PrintableBook.Core.Application.Updates;

public interface IUpdateFeed
{
    ValueTask<UpdateInfo?> GetLatestStableAsync(
        CancellationToken cancellationToken = default);
}
