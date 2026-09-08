using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class UpdateStorageLayoutTests
{
    [Fact]
    public void VersionPathsUseThreePartVersion()
    {
        var root = Path.Combine("C:", "test-root");
        var layout = new UpdateStorageLayout(new StubRootProvider(root));

        Assert.Equal(
            Path.Combine(root, "downloads", "0.1.2"),
            layout.GetDownloadDirectory(new Version(0, 1, 2)));
        Assert.Equal(
            Path.Combine(root, "staging", "0.1.2"),
            layout.GetStagingVersionDirectory(new Version(0, 1, 2)));
        Assert.Equal(
            Path.Combine(root, "staging", "0.1.2", "payload"),
            layout.GetReadyPayloadDirectory(new Version(0, 1, 2)));
    }

    [Fact]
    public void VersionPathsIgnoreRevision()
    {
        var layout = new UpdateStorageLayout(new StubRootProvider("test-root"));

        Assert.EndsWith(Path.Combine("downloads", "0.1.2"), layout.GetDownloadDirectory(new Version(0, 1, 2, 99)));
    }

    [Fact]
    public void TemporaryExtractionDirectoriesAreUniqueStagingChildren()
    {
        var layout = new UpdateStorageLayout(new StubRootProvider("test-root"));
        var version = new Version(0, 1, 2);

        var first = layout.CreateTemporaryExtractionDirectory(version);
        var second = layout.CreateTemporaryExtractionDirectory(version);

        Assert.StartsWith(layout.GetStagingVersionDirectory(version), first, StringComparison.Ordinal);
        Assert.StartsWith(".extracting-", Path.GetFileName(first), StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    private sealed class StubRootProvider(string root) : IUpdateStorageRootProvider
    {
        public string RootPath { get; } = root;
    }
}
