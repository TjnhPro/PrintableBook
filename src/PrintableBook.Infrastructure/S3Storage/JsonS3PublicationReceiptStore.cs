using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.S3Storage;

namespace PrintableBook.Infrastructure.S3Storage;

public sealed class JsonS3PublicationReceiptStore(IFileSystem fileSystem) : IS3PublicationReceiptStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public async ValueTask<S3PublicationReceiptLoadResult> LoadAsync(DirectoryReference bookDirectory, CancellationToken cancellationToken = default)
    {
        var file = FileFor(bookDirectory);
        if (!await fileSystem.FileExistsAsync(file, cancellationToken)) return new(null);
        try
        {
            var receipt = JsonSerializer.Deserialize<S3PublicationReceipt>(await fileSystem.ReadTextAsync(file, cancellationToken), Options);
            if (receipt is null || receipt.Version != 1) return new(null, "s3_receipt_unavailable");
            if (receipt.Outcome == S3StorageOutcome.Running)
            {
                var interrupted = receipt with
                {
                    Outcome = S3StorageOutcome.Interrupted,
                    View = receipt.View with
                    {
                        Outcome = S3StorageOutcome.Interrupted,
                        Phase = "interrupted",
                        Files = receipt.View.Files.Select(row => row.State == S3StorageFileState.Pending ? row with { State = S3StorageFileState.Unknown } : row).ToArray(),
                        WarningCode = "s3_publication_interrupted"
                    },
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    FinishedAtUtc = DateTimeOffset.UtcNow
                };
                await SaveAsync(bookDirectory, interrupted, cancellationToken);
                return new(interrupted);
            }
            return new(receipt);
        }
        catch (JsonException) { return new(null, "s3_receipt_unavailable"); }
    }

    public ValueTask SaveAsync(DirectoryReference bookDirectory, S3PublicationReceipt receipt, CancellationToken cancellationToken = default) =>
        fileSystem.WriteTextAtomicallyAsync(FileFor(bookDirectory), JsonSerializer.Serialize(receipt, Options), cancellationToken);

    private static FileReference FileFor(DirectoryReference directory) => new(Path.Combine(directory.Value, ".workspace", "s3-publication.json"));
}
