using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class CacheCleanupArtifactRegressionTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.CacheCleanupRegression.{Guid.NewGuid():N}");

    [Fact]
    // Regression: ISSUE-001 — Clear Cache left trim, canvas, resize, and frame artifacts behind.
    // Found by /qa on 2026-08-25.
    // Report: .gstack/qa-reports/qa-report-printablebook-desktop-2026-08-25.md
    public async Task ClearHeavyProcessingCacheAsync_removes_all_non_metadata_page_cache_artifacts()
    {
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("Book"),
            new DirectoryReference(Path.Combine(rootPath, "Book")));
        var pageCache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-0001");
        var classification = Path.Combine(pageCache, "classification.json");
        var stamp = Path.Combine(pageCache, "input-stamp.json");
        var artifacts = new[]
        {
            Path.Combine(pageCache, "trim.png"),
            Path.Combine(pageCache, "canvas.png"),
            Path.Combine(pageCache, "resize.png"),
            Path.Combine(pageCache, "frame.png"),
            Path.Combine(pageCache, "future-stage", "rendered.png")
        };

        Directory.CreateDirectory(pageCache);
        await File.WriteAllTextAsync(classification, "classification");
        await File.WriteAllTextAsync(stamp, "stamp");
        for (var index = 0; index < artifacts.Length; index++)
        {
            var artifact = artifacts[index];
            Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
            await File.WriteAllBytesAsync(artifact, new byte[(index + 1) * 11]);
        }

        var freed = await new PhysicalBookStorageMaintenance().ClearHeavyProcessingCacheAsync(workspace);

        Assert.Equal(165, freed);
        Assert.True(File.Exists(classification));
        Assert.True(File.Exists(stamp));
        Assert.All(artifacts, artifact => Assert.False(File.Exists(artifact), artifact));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
