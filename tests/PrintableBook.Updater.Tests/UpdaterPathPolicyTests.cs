using PrintableBook.Updater;

namespace PrintableBook.Updater.Tests;

public sealed class UpdaterPathPolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidateAcceptsWorkerInStagedPayloadIncludingCaseDifferences()
    {
        var command = CreateCommand();

        new UpdaterPathPolicy().Validate(command, Path.Combine(command.PayloadDirectory.ToUpperInvariant(), "printablebook.updater.exe"));
    }

    [Theory]
    [InlineData("relative-app", "payload", "updates", "worker")]
    public void ValidateRejectsRelativePaths(string app, string payload, string updates, string worker)
    {
        Assert.Throws<ArgumentException>(() => new UpdaterPathPolicy().Validate(
            new UpdaterCommand(1, app, payload, updates, new Version(0, 2, 0)), worker));
    }

    [Fact]
    public void ValidateRejectsUnsafeTopologyAndWorkerLocations()
    {
        var valid = CreateCommand();
        var policy = new UpdaterPathPolicy();
        var cases = new[]
        {
            valid with { UpdatesRoot = valid.AppRoot },
            valid with { UpdatesRoot = Path.Combine(valid.AppRoot, "Updates") },
            valid with { AppRoot = Path.Combine(valid.UpdatesRoot, "App") },
            valid with { PayloadDirectory = Path.Combine(root, "outside") },
            valid with { PayloadDirectory = Path.Combine(valid.UpdatesRoot, "downloads", "payload") },
        };

        foreach (var command in cases)
        {
            Assert.Throws<ArgumentException>(() => policy.Validate(command, Path.Combine(valid.PayloadDirectory, "PrintableBook.Updater.exe")));
        }

        foreach (var worker in new[]
        {
            Path.Combine(valid.AppRoot, "PrintableBook.Updater.exe"),
            Path.Combine(valid.UpdatesRoot, "staging", "other", "PrintableBook.Updater.exe"),
            Path.Combine(valid.PayloadDirectory, "another.exe"),
            Path.Combine(valid.PayloadDirectory, "nested", "PrintableBook.Updater.exe"),
        })
        {
            Assert.Throws<ArgumentException>(() => policy.Validate(valid, worker));
        }
    }

    [Fact]
    public void ValidateRejectsAppRootUnderDriveRootUpdatesDirectory()
    {
        var driveRoot = Path.GetPathRoot(root)!;
        var command = new UpdaterCommand(
            1,
            Path.Combine(driveRoot, "PrintableBook-App"),
            Path.Combine(driveRoot, "staging", "0.2.1", "payload"),
            driveRoot,
            new Version(0, 2, 0));

        Assert.Throws<ArgumentException>(() => new UpdaterPathPolicy().Validate(command, Path.Combine(command.PayloadDirectory, "PrintableBook.Updater.exe")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private UpdaterCommand CreateCommand()
    {
        var updates = Path.Combine(root, "Updates");
        return new UpdaterCommand(1, Path.Combine(root, "App"), Path.Combine(updates, "staging", "0.2.1", "payload"), updates, new Version(0, 2, 0));
    }
}
