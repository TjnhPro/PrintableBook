namespace PrintableBook.Core.Application.Updates;

public interface IUpdatePreparationService
{
    ValueTask<PreparedUpdate> PrepareAsync(
        UpdateInfo update,
        IProgress<UpdatePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
