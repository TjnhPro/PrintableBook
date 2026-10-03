using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.S3Storage;

namespace PrintableBook.Infrastructure.Tests.S3Storage;

public sealed class JsonBookPublicationPackageStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.S3Package.{Guid.NewGuid():N}");

    [Fact]
    public async Task Refresh_persists_all_contract_rows_with_recovery_actions_and_current_file_facts()
    {
        const string bookId = "Book One";
        var bookDirectory = new DirectoryReference(root);
        var outputDirectory = new DirectoryReference(Path.Combine(root, "Output"));
        Directory.CreateDirectory(outputDirectory.Value);
        var artifacts = BookOutputArtifactContract.ForBook(bookId);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory.Value, artifacts[0].FileName), "cover");
        var store = new JsonBookPublicationPackageStore(new PhysicalFileSystem());

        var refreshed = await store.RefreshAsync(bookId, bookDirectory, outputDirectory);
        var loaded = await store.LoadAsync(bookDirectory);

        Assert.Equal(7, refreshed.Artifacts.Count);
        Assert.False(refreshed.IsComplete);
        Assert.True(refreshed.Artifacts[0].Exists);
        Assert.Equal(5, refreshed.Artifacts[0].Length);
        Assert.Contains("Cover", refreshed.Artifacts[0].RecoveryAction, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(loaded);
        Assert.Equal(refreshed.BookId, loaded!.BookId);
        Assert.Equal(refreshed.Artifacts, loaded.Artifacts);
        Assert.True(File.Exists(Path.Combine(root, ".workspace", "s3-publication-package.json")));
    }

    [Fact]
    public async Task Complete_package_requires_exactly_all_seven_artifacts()
    {
        const string bookId = "Book Complete";
        var outputDirectory = new DirectoryReference(Path.Combine(root, "Output"));
        Directory.CreateDirectory(outputDirectory.Value);
        foreach (var artifact in BookOutputArtifactContract.ForBook(bookId))
            await File.WriteAllTextAsync(Path.Combine(outputDirectory.Value, artifact.FileName), artifact.FileName);
        var store = new JsonBookPublicationPackageStore(new PhysicalFileSystem());

        var package = await store.RefreshAsync(bookId, new DirectoryReference(root), outputDirectory);

        Assert.True(package.IsComplete);
        Assert.All(package.Artifacts, artifact => Assert.True(artifact.Exists));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
    }
}
