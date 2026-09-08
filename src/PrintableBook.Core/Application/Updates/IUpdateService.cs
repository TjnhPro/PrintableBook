namespace PrintableBook.Core.Application.Updates;

public interface IUpdateService
{
    ValueTask<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default);
}
