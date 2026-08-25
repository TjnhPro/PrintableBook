using System.Security.Cryptography;
using System.Text;
using System.IO;
using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.BackgroundTasks.Workers;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Desktop.Preview;

public sealed class BookAssetPreviewCoordinator(IBackgroundTaskManager taskManager)
{
    public ValueTask<BackgroundTaskSnapshot> StartAsync(string bookId, string sourceReference, CancellationToken cancellationToken = default) =>
        taskManager.StartAsync(
            BackgroundTaskKind.AssetPreview,
            BuildKey(bookId, sourceReference),
            $"{bookId}/{Path.GetFileName(sourceReference)}",
            new AssetPreviewRequest(bookId, sourceReference),
            cancellationToken: cancellationToken);

    public bool TryGetResult(BackgroundTaskId taskId, out BookAssetPreview? preview) =>
        taskManager.TryGetResult(taskId, out preview);

    public ValueTask<BackgroundTaskSnapshot?> GetTaskAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) =>
        taskManager.GetAsync(taskId, cancellationToken);

    private static string BuildKey(string bookId, string sourceReference)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{bookId}\0{sourceReference}")));
        return $"preview:{bookId}:{hash}";
    }
}
