using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.S3Storage;

namespace PrintableBook.Infrastructure.Tests.S3Storage;

public sealed class JsonS3PublicationReceiptStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.S3Receipt.{Guid.NewGuid():N}");

    [Fact]
    public async Task Load_converts_a_running_receipt_to_interrupted_with_unknown_pending_rows()
    {
        Directory.CreateDirectory(root);
        var directory = new DirectoryReference(root);
        var store = new JsonS3PublicationReceiptStore(new PhysicalFileSystem());
        var settings = new S3StorageSettings("access", "secret", new("us-east-1", "valid-bucket", "coloring"));
        var view = S3StorageBookView.Pending("Book One", "B0FCC8Q6JP", S3StorageAction.Upload, settings) with
        {
            Outcome = S3StorageOutcome.Running,
            Phase = "publishing"
        };
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.SaveAsync(directory, new(1, "Book One", "B0FCC8Q6JP", S3StorageAction.Upload, S3StorageOutcome.Running, "revision", view, started, started));

        var loaded = await store.LoadAsync(directory);

        Assert.NotNull(loaded);
        Assert.Equal(S3StorageOutcome.Interrupted, loaded!.Outcome);
        Assert.Equal(S3StorageOutcome.Interrupted, loaded.View.Outcome);
        Assert.Equal("s3_publication_interrupted", loaded.View.WarningCode);
        Assert.All(loaded.View.Files, row => Assert.Equal(S3StorageFileState.Unknown, row.State));
        Assert.NotNull(loaded.FinishedAtUtc);
    }

    [Fact]
    public async Task Corrupt_receipt_is_recoverable_and_does_not_block_a_new_check()
    {
        var workspace = Path.Combine(root, ".workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "s3-publication.json"), "not-json");
        var store = new JsonS3PublicationReceiptStore(new PhysicalFileSystem());

        Assert.Null(await store.LoadAsync(new DirectoryReference(root)));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
    }
}
